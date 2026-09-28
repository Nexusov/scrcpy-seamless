using ScrcpySeamless.Core.Adb;
using ScrcpySeamless.Infrastructure.Adb;
using Xunit;

namespace ScrcpySeamless.Infrastructure.Tests.Adb;

/** Checks bounded USB route enrichment against synthetic ADB server replies. */
public sealed class AdbGatewayUsbEvidenceTests
{
    private const string MissingUsbPath = """
        List of devices attached
        SYNTHETIC_USB device product:synthetic model:Test_Phone transport_id:24
        192.0.2.8:5555 device model:Test_Phone transport_id:22
        """;

    /** A USB-scoped transport ID identifies only its exact current listing row. */
    [Fact]
    public async Task ScopedTransportIdResolvesOnlyMatchingUnknownRoute()
    {
        ScriptedRunner runner = new([Listing(MissingUsbPath), Listing(MissingUsbPath)],
            _ => Task.FromResult(Reply("24\n")));
        AdbResult<IReadOnlyList<AdbDevice>> result = await new AdbGateway(runner)
            .GetDevicesAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(AdbTransportKind.Usb, result.Value![0].TransportKind);
        Assert.Equal((ulong)24, result.Value[0].TransportId);
        Assert.Equal(AdbTransportKind.Network, result.Value[1].TransportKind);
        Assert.Equal(["devices -l", "-d transport-id", "devices -l"], runner.Calls);
    }

    /** A fresh listing after the probe prevents publishing a detached route. */
    [Fact]
    public async Task UnplugDuringProbeDoesNotPublishTheOldUsbRoute()
    {
        ScriptedRunner runner = new([Listing(MissingUsbPath), Listing("List of devices attached\n")],
            _ => Task.FromResult(Reply("24\n")));
        AdbResult<IReadOnlyList<AdbDevice>> result = await new AdbGateway(runner)
            .GetDevicesAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    /** A second USB transport makes the scoped query fail rather than picking a row. */
    [Fact]
    public async Task MultipleUsbDevicesRemainUnknownWhenScopedQueryFails()
    {
        const string output = """
            List of devices attached
            USB_ONE device model:Test_Phone transport_id:24
            USB_TWO device model:Test_Phone transport_id:25
            """;
        ScriptedRunner runner = new([Listing(output)], _ => Task.FromResult(
            new AdbProcessResult(1, string.Empty, "adb: more than one device", false)));
        AdbResult<IReadOnlyList<AdbDevice>> result = await new AdbGateway(runner)
            .GetDevicesAsync(TestContext.Current.CancellationToken);

        Assert.All(result.Value!, device => Assert.Equal(AdbTransportKind.Unknown, device.TransportKind));
        Assert.Equal(1, runner.ProbeCalls);
    }

    /** A USB ID outside the current snapshot cannot label any displayed route. */
    [Fact]
    public async Task UnmatchedScopedTransportIdLeavesRoutesUnknown()
    {
        ScriptedRunner runner = new([Listing(MissingUsbPath)],
            _ => Task.FromResult(Reply("99\n")));
        AdbResult<IReadOnlyList<AdbDevice>> result = await new AdbGateway(runner)
            .GetDevicesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AdbTransportKind.Unknown, result.Value![0].TransportKind);
        Assert.Equal(AdbTransportKind.Network, result.Value[1].TransportKind);
        Assert.Equal(1, runner.Calls.Count(command => command == "devices -l"));
    }

    /** Duplicate snapshot transport IDs or selectors cannot identify one profile target. */
    [Theory]
    [InlineData("USB_ONE device transport_id:24\nUSB_TWO device transport_id:24")]
    [InlineData("DUPLICATE device transport_id:24\nDUPLICATE device transport_id:25")]
    public async Task AmbiguousSnapshotLeavesEveryRouteUnknown(string rows)
    {
        ScriptedRunner runner = new([Listing("List of devices attached\n" + rows)],
            _ => Task.FromResult(Reply("24\n")));
        AdbResult<IReadOnlyList<AdbDevice>> result = await new AdbGateway(runner)
            .GetDevicesAsync(TestContext.Current.CancellationToken);

        Assert.All(result.Value!, device => Assert.Equal(AdbTransportKind.Unknown, device.TransportKind));
        Assert.Equal(1, runner.ProbeCalls);
        Assert.Equal(1, runner.Calls.Count(command => command == "devices -l"));
    }

    /** Empty, invalid, unsuccessful and diagnostic probe replies give no positive evidence. */
    [Theory]
    [InlineData(0, "", "", false)]
    [InlineData(0, "unknown\n", "", false)]
    [InlineData(0, "24\n25\n", "", false)]
    [InlineData(0, "not-a-number\n", "", false)]
    [InlineData(0, "24\n", "error: synthetic failure", false)]
    [InlineData(0, "24\n", "", true)]
    [InlineData(1, "24\n", "", false)]
    public async Task MalformedOrFailedProbeCannotProveUsb(int exitCode, string output,
        string error, bool truncated)
    {
        ScriptedRunner runner = new([Listing(MissingUsbPath)], _ => Task.FromResult(
            new AdbProcessResult(exitCode, output, error, truncated)));
        AdbResult<IReadOnlyList<AdbDevice>> result = await new AdbGateway(runner)
            .GetDevicesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AdbTransportKind.Unknown, result.Value![0].TransportKind);
        Assert.Equal(AdbTransportKind.Network, result.Value[1].TransportKind);
        Assert.Equal(1, runner.ProbeCalls);
    }

    /** A timed-out optional probe leaves the user an explicit unresolved route. */
    [Fact]
    public async Task ProbeTimeoutLeavesRouteUnknown()
    {
        ScriptedRunner runner = new([Listing(MissingUsbPath)],
            _ => Task.FromException<AdbProcessResult>(new TimeoutException("Synthetic timeout")));
        AdbResult<IReadOnlyList<AdbDevice>> result = await new AdbGateway(runner)
            .GetDevicesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AdbTransportKind.Unknown, result.Value![0].TransportKind);
    }

    /** An optional USB probe I/O failure retains an honest unknown route. */
    [Fact]
    public async Task ProbeIoFailureLeavesRouteUnknown()
    {
        ScriptedRunner runner = new([Listing(MissingUsbPath)],
            _ => Task.FromException<AdbProcessResult>(new IOException("Synthetic probe failure")));
        AdbResult<IReadOnlyList<AdbDevice>> result = await new AdbGateway(runner)
            .GetDevicesAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(AdbTransportKind.Unknown, result.Value![0].TransportKind);
    }

    /** A failed confirmation cannot publish the older device listing as fresh. */
    [Theory]
    [InlineData(1, "List of devices attached\n", "error: synthetic failure", false)]
    [InlineData(0, "List of devices attached\nmalformed", "", false)]
    [InlineData(0, "List of devices attached\n", "", true)]
    public async Task FailedRecheckRejectsTheOldSnapshot(int exitCode, string output,
        string error, bool truncated)
    {
        AdbProcessResult recheck = new(exitCode, output, error, truncated);
        ScriptedRunner runner = new([Listing(MissingUsbPath)],
            _ => Task.FromResult(Reply("24\n")),
            () => Task.FromResult(recheck));
        AdbResult<IReadOnlyList<AdbDevice>> result = await new AdbGateway(runner)
            .GetDevicesAsync(TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
    }

    /** Confirmation timeout or I/O failure cannot make an old route authoritative. */
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RecheckExceptionRejectsTheOldSnapshot(bool timeout)
    {
        Exception error = timeout
            ? new TimeoutException("Synthetic timeout")
            : new IOException("Synthetic I/O failure");
        ScriptedRunner runner = new([Listing(MissingUsbPath)],
            _ => Task.FromResult(Reply("24\n")),
            () => Task.FromException<AdbProcessResult>(error));
        AdbResult<IReadOnlyList<AdbDevice>> result = await new AdbGateway(runner)
            .GetDevicesAsync(TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
    }

    /** USB route evidence never changes the ADB authorization/availability state. */
    [Fact]
    public async Task ScopedEvidencePreservesOfflineState()
    {
        const string output = "List of devices attached\nOFFLINE_USB offline transport_id:24\n";
        ScriptedRunner runner = new([Listing(output), Listing(output)],
            _ => Task.FromResult(Reply("24\n")));
        AdbResult<IReadOnlyList<AdbDevice>> result = await new AdbGateway(runner)
            .GetDevicesAsync(TestContext.Current.CancellationToken);

        AdbDevice device = Assert.Single(result.Value!);
        Assert.Equal(AdbTransportKind.Usb, device.TransportKind);
        Assert.Equal(AdbDeviceState.Offline, device.State);
    }

    /** A malformed or ID-less listing is not promoted by a USB-looking selector. */
    [Theory]
    [InlineData("List of devices attached\nUSB_LOOKING device model:Test_Phone")]
    [InlineData("List of devices attached\nUSB_LOOKING device transport_id:invalid")]
    [InlineData("List of devices attached\nUSB_LOOKING device transport_id:24 transport_id:25")]
    [InlineData("List of devices attached\nUSB_LOOKING device transport_id:24\nmalformed")]
    public async Task IncompleteListingDoesNotTriggerProbe(string output)
    {
        ScriptedRunner runner = new([Listing(output)],
            _ => throw new InvalidOperationException("Probe must not run"));
        AdbResult<IReadOnlyList<AdbDevice>> result = await new AdbGateway(runner)
            .GetDevicesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AdbTransportKind.Unknown, Assert.Single(result.Value!).TransportKind);
        Assert.Equal(0, runner.ProbeCalls);
    }

    /** No listed transport produces no USB probe and no invented device. */
    [Fact]
    public async Task EmptyListingDoesNotTriggerUsbProbe()
    {
        ScriptedRunner runner = new([Listing("List of devices attached\n")],
            _ => throw new InvalidOperationException("Probe must not run"));
        AdbResult<IReadOnlyList<AdbDevice>> result = await new AdbGateway(runner)
            .GetDevicesAsync(TestContext.Current.CancellationToken);

        Assert.Empty(result.Value!);
        Assert.Equal(0, runner.ProbeCalls);
    }

    /** Explicit metadata retains USB kind independently from authorization state. */
    [Fact]
    public async Task ExplicitUsbPathAndUnauthorizedStateNeedNoProbe()
    {
        const string output = "List of devices attached\nEXPLICIT_USB unauthorized usb:1-2 transport_id:24\n";
        ScriptedRunner runner = new([Listing(output)],
            _ => throw new InvalidOperationException("Probe must not run"));
        AdbResult<IReadOnlyList<AdbDevice>> result = await new AdbGateway(runner)
            .GetDevicesAsync(TestContext.Current.CancellationToken);

        AdbDevice device = Assert.Single(result.Value!);
        Assert.Equal(AdbTransportKind.Usb, device.TransportKind);
        Assert.Equal(AdbDeviceState.Unauthorized, device.State);
        Assert.Equal(0, runner.ProbeCalls);
    }

    /** Endpoint grammar and emulator-like names do not turn into USB guesses. */
    [Fact]
    public async Task NetworkAndEmulatorSelectorsStayDistinctWithoutScopedUsbEvidence()
    {
        const string output = """
            List of devices attached
            192.0.2.8:5555 device transport_id:1
            [2001:db8::8]:5555 device transport_id:2
            phone.example:5555 device transport_id:3
            adb-synthetic._adb-tls-connect._tcp device transport_id:4
            emulator-5554 device transport_id:5
            """;
        ScriptedRunner runner = new([Listing(output)], _ => Task.FromResult(
            new AdbProcessResult(1, string.Empty, "no USB device", false)));
        AdbResult<IReadOnlyList<AdbDevice>> result = await new AdbGateway(runner)
            .GetDevicesAsync(TestContext.Current.CancellationToken);

        Assert.All(result.Value!.Take(4), device =>
            Assert.Equal(AdbTransportKind.Network, device.TransportKind));
        Assert.Equal(AdbTransportKind.Unknown, result.Value![4].TransportKind);
    }

    /** Synthetic ADB command responses with no process or shared-server access. */
    private sealed class ScriptedRunner(
        IEnumerable<AdbProcessResult> listings,
        Func<CancellationToken, Task<AdbProcessResult>> probe,
        Func<Task<AdbProcessResult>>? secondListing = null) : IAdbProcessRunner
    {
        private readonly Queue<AdbProcessResult> pendingListings = new(listings);
        public List<string> Calls { get; } = [];
        public int ProbeCalls { get; private set; }
        private int listingCalls;

        public Task<AdbProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout,
            CancellationToken cancellationToken, string? standardInput = null)
        {
            string command = string.Join(' ', arguments);
            Calls.Add(command);

            if (command == "devices -l")
            {
                listingCalls++;

                if (listingCalls == 2 && secondListing is not null)
                {
                    return secondListing();
                }

                return Task.FromResult(pendingListings.Dequeue());
            }

            if (command == "-d transport-id")
            {
                ProbeCalls++;
                return probe(cancellationToken);
            }

            throw new InvalidOperationException($"Unexpected synthetic ADB command: {command}");
        }
    }

    /** Wraps a sanitized ADB listing as a successful process result. */
    private static AdbProcessResult Listing(string output) => new(0, output, string.Empty, false);

    /** Wraps a synthetic USB-scoped reply as a successful process result. */
    private static AdbProcessResult Reply(string output) => new(0, output, string.Empty, false);
}
