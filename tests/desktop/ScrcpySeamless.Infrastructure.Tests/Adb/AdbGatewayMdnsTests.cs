using ScrcpySeamless.Core.Adb;
using ScrcpySeamless.Infrastructure.Adb;
using Xunit;

namespace ScrcpySeamless.Infrastructure.Tests.Adb;

/** Checks the reported empty discovery and unavailable daemon without launching ADB. */
public sealed class AdbGatewayMdnsTests
{
    /** An empty service list is not authoritative when the server reports mDNS unavailable. */
    [Fact]
    public async Task EmptyServicesWithUnavailableMdnsAreAnError()
    {
        FakeRunner runner = new(
            new AdbProcessResult(0, "List of discovered mdns services\n", string.Empty, false),
            new AdbProcessResult(0, "ERROR: mdns daemon unavailable\n", string.Empty, false));
        AdbGateway gateway = new(runner);

        AdbResult<IReadOnlyList<AdbMdnsService>> result =
            await gateway.GetServicesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AdbFailureKind.MdnsUnavailable, result.Failure);
        Assert.Equal(["mdns services", "mdns check"], runner.Calls);
    }

    /** A working mDNS backend can legitimately report no advertised service. */
    [Fact]
    public async Task HealthyMdnsPreservesAnAuthoritativeEmptyResult()
    {
        FakeRunner runner = new(
            new AdbProcessResult(0, "List of discovered mdns services\n", string.Empty, false),
            new AdbProcessResult(0, "mdns daemon version [Openscreen discovery 0.0.0]\n",
                string.Empty, false));
        AdbGateway gateway = new(runner);

        AdbResult<IReadOnlyList<AdbMdnsService>> result =
            await gateway.GetServicesAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
        Assert.Equal(["mdns services", "mdns check"], runner.Calls);
    }

    /** A reported pairing service needs no extra health query. */
    [Fact]
    public async Task DiscoveredPairingServiceDoesNotProbeAgain()
    {
        FakeRunner runner = new(new AdbProcessResult(0, """
            List of discovered mdns services
            adb-SYNTHETIC-pair._adb-tls-pairing._tcp. _adb-tls-pairing._tcp. 192.0.2.8:37123
            """, string.Empty, false));
        AdbGateway gateway = new(runner);

        AdbResult<IReadOnlyList<AdbMdnsService>> result =
            await gateway.GetServicesAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(AdbServiceKind.Pairing, Assert.Single(result.Value!).Kind);
        Assert.Equal(["mdns services"], runner.Calls);
    }

    /** Unknown health output never becomes a success or a guessed backend failure. */
    [Fact]
    public async Task UnknownMdnsCheckResponseRemainsUncertain()
    {
        FakeRunner runner = new(
            new AdbProcessResult(0, "List of discovered mdns services\n", string.Empty, false),
            new AdbProcessResult(0, "unexpected synthetic response\n", string.Empty, false));
        AdbGateway gateway = new(runner);

        AdbResult<IReadOnlyList<AdbMdnsService>> result =
            await gateway.GetServicesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AdbFailureKind.MalformedResponse, result.Failure);
    }

    /** Supplies bounded synthetic command output without a shared ADB server. */
    private sealed class FakeRunner(params AdbProcessResult[] responses) : IAdbProcessRunner
    {
        private readonly Queue<AdbProcessResult> pending = new(responses);
        public List<string> Calls { get; } = [];

        public Task<AdbProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout,
            CancellationToken cancellationToken, string? standardInput = null)
        {
            Calls.Add(string.Join(' ', arguments));
            return Task.FromResult(pending.Dequeue());
        }
    }
}
