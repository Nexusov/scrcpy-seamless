using System.Diagnostics;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Adb;
using ScrcpySeamless.Infrastructure.Adb;
using Xunit;

namespace ScrcpySeamless.Infrastructure.Tests.Adb;

/** Checks untrusted discovery output and process argument isolation. */
public sealed class AdbTests
{
    [Fact]
    public void DevicesIncludeDistinctAuthorizationStatesAndNetworkTransports()
    {
        const string output = """
            * daemon started successfully
            List of devices attached
            USB_EXAMPLE_1 device product:synthetic model:Test_Phone transport_id:1
            USB_EXAMPLE_2 unauthorized transport_id:2
            192.0.2.8:5555 offline transport_id:3
            [2001:db8::8]:5555 device transport_id:4
            malformed
            """;

        AdbParseResult<AdbDevice> parsed = AdbResponseParser.ParseDevices(output);

        Assert.Equal(4, parsed.Items.Count);
        Assert.Equal(1, parsed.MalformedLineCount);
        Assert.Equal(AdbDeviceState.Unauthorized, parsed.Items[1].State);
        Assert.Equal(AdbDeviceState.Offline, parsed.Items[2].State);
        Assert.Equal("Test Phone", parsed.Items[0].Model);
        Assert.Equal("[2001:db8::8]:5555", parsed.Items[3].Serial);
    }

    [Fact]
    public void MdnsServicesKeepPairingAndConnectionEndpointsSeparate()
    {
        const string output = """
            * daemon not running; starting now at tcp:5037
            List of discovered mdns services
            adb-SYNTHETIC-pair._adb-tls-pairing._tcp. _adb-tls-pairing._tcp. 192.0.2.8:37123
            adb-SYNTHETIC-connect._adb-tls-connect._tcp. _adb-tls-connect._tcp. [2001:db8::8]:37124
            broken _adb-tls-connect._tcp. 2001:db8::8:37124
            """;

        AdbParseResult<AdbMdnsService> parsed = AdbResponseParser.ParseMdnsServices(output);

        Assert.Equal(2, parsed.Items.Count);
        Assert.Equal(1, parsed.MalformedLineCount);
        Assert.Equal(AdbServiceKind.Pairing, parsed.Items[0].Kind);
        Assert.Equal(37123, parsed.Items[0].Endpoint.Port);
        Assert.Equal(AdbServiceKind.Connection, parsed.Items[1].Kind);
        Assert.Equal(37124, parsed.Items[1].Endpoint.Port);
    }

    [Fact]
    public void PairAndConnectRequireSuccessForTheExpectedEndpoint()
    {
        NetworkEndpoint endpoint = NetworkEndpoint.Parse("192.0.2.8:5555");

        Assert.True(AdbResponseParser.IsPairingSuccessful(0, "Successfully paired to 192.0.2.8:37123"));
        Assert.False(AdbResponseParser.IsPairingSuccessful(1, "Successfully paired to 192.0.2.8:37123"));
        Assert.False(AdbResponseParser.IsPairingSuccessful(0, "Failed: Wrong password"));
        Assert.True(AdbResponseParser.IsConnectSuccessful(0, "already connected to 192.0.2.8:5555", endpoint));
        Assert.False(AdbResponseParser.IsConnectSuccessful(0, "connected to 192.0.2.9:5555", endpoint));
        Assert.False(AdbResponseParser.IsConnectSuccessful(1, "connected to 192.0.2.8:5555", endpoint));
    }

    /** Error diagnostics remain detectable without treating daemon notices as failures. */
    [Fact]
    public void StderrDistinguishesErrorsFromDaemonStartup()
    {
        Assert.False(AdbResponseParser.HasErrorDiagnostics("* daemon not running; starting now at tcp:5037"));
        Assert.True(AdbResponseParser.HasErrorDiagnostics("adb: error: device unauthorized"));
        Assert.True(AdbResponseParser.HasErrorDiagnostics("error: failed to connect"));
    }

    [Fact]
    public void ProcessInvocationKeepsArgumentsSeparateFromShell()
    {
        var runner = new AdbProcessRunner(@"C:\synthetic\adb.exe");
        var startInfo = runner.CreateStartInfo(["pair", "[2001:db8::8]:37123"], redirectStandardInput: true);

        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.True(startInfo.RedirectStandardInput);
        Assert.Equal(2, startInfo.ArgumentList.Count);
        Assert.Equal("[2001:db8::8]:37123", startInfo.ArgumentList[1]);
        Assert.Equal("1", startInfo.Environment["ADB_MDNS_OPENSCREEN"]);
    }

    [Fact]
    public async Task CancelledInvocationDoesNotStartAChild()
    {
        var runner = new AdbProcessRunner(@"C:\synthetic\missing-adb.exe");
        using CancellationTokenSource cancellationSource = new();
        await cancellationSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
            ["devices", "-l"],
            TimeSpan.FromSeconds(1),
            cancellationSource.Token));
    }

    [Fact]
    public async Task CancellationTerminatesTheOwnedChildAfterItStarts()
    {
        (string directory, string script, string marker) = CreateBlockingChild();
        using CancellationTokenSource cancellationSource = new();
        Task<AdbProcessResult>? runTask = null;

        try
        {
            var runner = new AdbProcessRunner("powershell.exe");
            runTask = runner.RunAsync(
                ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, marker],
                TimeSpan.FromSeconds(20),
                cancellationSource.Token);
            int childPid = await WaitForChildMarkerAsync(marker, runTask);
            await cancellationSource.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);
            Assert.False(IsProcessAlive(childPid));
        }
        finally
        {
            await cancellationSource.CancelAsync();

            if (runTask is not null)
            {
                try
                {
                    await runTask;
                }
                catch (OperationCanceledException)
                {
                    // Cancellation is the expected cleanup path for the blocking child.
                }
            }

            DeleteBlockingChild(directory, script, marker);
        }
    }

    [Fact]
    public async Task TimeoutTerminatesAnOwnedChild()
    {
        (string directory, string script, string marker) = CreateBlockingChild();

        try
        {
            var runner = new AdbProcessRunner("powershell.exe");
            await Assert.ThrowsAsync<TimeoutException>(() => runner.RunAsync(
                ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, marker],
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken));

            if (File.Exists(marker))
            {
                int childPid = int.Parse(await File.ReadAllTextAsync(marker, TestContext.Current.CancellationToken));
                Assert.False(IsProcessAlive(childPid));
            }
        }
        finally
        {
            DeleteBlockingChild(directory, script, marker);
        }
    }

    [Fact]
    public async Task InvalidPairingCodeNeverInvokesAdb()
    {
        var gateway = new FakeAdbGateway();
        var service = new AdbPairingService(gateway);

        AdbResult<AdbPairingOutcome> result = await service.PairAsync(
            NetworkEndpoint.Parse("192.0.2.8:37123"),
            "bad-code",
            NetworkEndpoint.Parse("192.0.2.8:37124"),
            null,
            CancellationToken.None);

        Assert.Equal(AdbFailureKind.InvalidInput, result.Failure);
        Assert.Equal(0, gateway.PairCount);
    }

    [Fact]
    public async Task PairingWithoutConnectionDoesNotInventAConnectionEndpoint()
    {
        var gateway = new FakeAdbGateway();
        var service = new AdbPairingService(gateway);

        AdbResult<AdbPairingOutcome> result = await service.PairAsync(
            NetworkEndpoint.Parse("192.0.2.8:37123"),
            "000000",
            null,
            null,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.Paired);
        Assert.Null(result.Value.ConnectedEndpoint);
        Assert.Null(result.Value.ConnectedSerial);
        Assert.Equal(1, gateway.PairCount);
        Assert.Equal(0, gateway.ConnectCount);
    }

    [Fact]
    public async Task PairingRejectsAConnectedDeviceWithDifferentUsbIdentity()
    {
        var gateway = new FakeAdbGateway();
        var service = new AdbPairingService(gateway);

        AdbResult<AdbPairingOutcome> result = await service.PairAsync(
            NetworkEndpoint.Parse("192.0.2.8:37123"),
            "000000",
            NetworkEndpoint.Parse("192.0.2.8:37124"),
            new UsbSerial("DIFFERENT_SYNTHETIC_SERIAL"),
            CancellationToken.None);

        Assert.Equal(AdbFailureKind.DeviceIdentityMismatch, result.Failure);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.Paired);
        Assert.Null(result.Value.ConnectedEndpoint);
        Assert.Equal(1, gateway.PairCount);
        Assert.Equal(1, gateway.ConnectCount);
        Assert.DoesNotContain("000000", result.ToString());
    }

    [Fact]
    public void ManualEndpointRejectsAmbiguousIpv6()
    {
        AdbResult<NetworkEndpoint> invalid = AdbDiscoveryService.ResolveManualEndpoint("2001:db8::8:5555");
        AdbResult<NetworkEndpoint> valid = AdbDiscoveryService.ResolveManualEndpoint("[2001:db8::8]:5555");

        Assert.Equal(AdbFailureKind.InvalidInput, invalid.Failure);
        Assert.True(valid.IsSuccess);
    }

    private sealed class FakeAdbGateway : IAdbGateway
    {
        public int PairCount { get; private set; }
        public int ConnectCount { get; private set; }

        public Task<AdbResult<IReadOnlyList<AdbDevice>>> GetDevicesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(AdbResult<IReadOnlyList<AdbDevice>>.Success([]));

        public Task<AdbResult<IReadOnlyList<AdbMdnsService>>> GetServicesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(AdbResult<IReadOnlyList<AdbMdnsService>>.Success([]));

        public Task<AdbResult<bool>> PairAsync(
            NetworkEndpoint pairingEndpoint,
            string pairingCode,
            CancellationToken cancellationToken)
        {
            PairCount++;
            return Task.FromResult(AdbResult<bool>.Success(true));
        }

        public Task<AdbResult<bool>> ConnectAsync(
            NetworkEndpoint connectionEndpoint,
            CancellationToken cancellationToken)
        {
            ConnectCount++;
            return Task.FromResult(AdbResult<bool>.Success(true));
        }

        public Task<AdbResult<UsbSerial>> GetConnectedSerialAsync(
            NetworkEndpoint connectionEndpoint,
            CancellationToken cancellationToken) =>
            Task.FromResult(AdbResult<UsbSerial>.Success(new UsbSerial("SYNTHETIC_SERIAL")));
    }

    private static (string Directory, string Script, string Marker) CreateBlockingChild()
    {
        string directory = Path.Combine(Path.GetTempPath(), "scrcpy-seamless-adb-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string script = Path.Combine(directory, "block.ps1");
        string marker = Path.Combine(directory, "ready.txt");
        File.WriteAllText(script, """
            param([string]$markerPath)
            [IO.File]::WriteAllText(($markerPath + '.tmp'), $PID.ToString())
            [IO.File]::Move(($markerPath + '.tmp'), $markerPath)
            [Threading.ManualResetEventSlim]::new($false).Wait()
            """);
        return (directory, script, marker);
    }

    private static async Task<int> WaitForChildMarkerAsync(string marker, Task<AdbProcessResult> runTask)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        TaskCompletionSource markerCreated = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using FileSystemWatcher watcher = new(Path.GetDirectoryName(marker)!, Path.GetFileName(marker));
        watcher.Created += (_, _) => markerCreated.TrySetResult();
        watcher.Renamed += (_, _) => markerCreated.TrySetResult();
        watcher.EnableRaisingEvents = true;

        if (!File.Exists(marker))
        {
            Task completed = await Task.WhenAny(markerCreated.Task, runTask).WaitAsync(deadline.Token);

            if (completed == runTask)
            {
                AdbProcessResult result = await runTask;
                throw new InvalidOperationException($"Test child exited early: {result.ExitCode}; {result.StandardError}");
            }
        }

        return int.Parse(await File.ReadAllTextAsync(marker, TestContext.Current.CancellationToken));
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void DeleteBlockingChild(string directory, string script, string marker)
    {
        File.Delete(marker);
        File.Delete(marker + ".tmp");
        File.Delete(script);
        Directory.Delete(directory);
    }
}
