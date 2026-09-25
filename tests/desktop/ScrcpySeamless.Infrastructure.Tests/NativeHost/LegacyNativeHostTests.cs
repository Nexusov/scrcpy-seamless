using System.Diagnostics;
using System.Text.Json;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Application.Connection;
using ScrcpySeamless.Core.Application.NativeHost;
using ScrcpySeamless.Core.Configuration;
using ScrcpySeamless.Infrastructure.NativeHost;
using Xunit;

namespace ScrcpySeamless.Infrastructure.Tests.NativeHost;

/// <summary>Protects the temporary native process boundary without contacting ADB or a device.</summary>
public sealed class LegacyNativeHostTests
{
    /// <summary>Launch translation uses the captured serial, discrete values and process-local paths.</summary>
    [Fact]
    public void StartInfoUsesExactBundlePathsAndCapturedOptions()
    {
        string directory = CreateDirectory();

        try
        {
            string nativePath = CreateFile(directory, "scrcpy.exe");
            string serverPath = CreateFile(directory, "scrcpy-server");
            string adbPath = CreateFile(directory, "adb.exe");
            LegacyNativeHost host = new(nativePath, serverPath, adbPath, Path.Combine(directory, "sessions"));
            NativeStartRequest request = CreateRequest(
                "SYNTHETIC_USB",
                NetworkEndpoint.Parse("synthetic.example:37123"),
                new Dictionary<string, JsonElement> { ["window-title"] = JsonSerializer.SerializeToElement("A window with spaces") });
            ProcessStartInfo startInfo = host.CreateStartInfo(request, "Local\\synthetic-stop");

            Assert.Equal(nativePath, startInfo.FileName);
            Assert.Equal(directory, startInfo.WorkingDirectory);
            Assert.False(startInfo.UseShellExecute);
            Assert.Equal(["-s", "SYNTHETIC_USB", "--window-title=A window with spaces", "--pause-on-exit=false"],
                startInfo.ArgumentList.ToArray());
            Assert.Equal(adbPath, startInfo.Environment["ADB"]);
            Assert.Equal(serverPath, startInfo.Environment["SCRCPY_SERVER_PATH"]);
            Assert.Equal("Local\\synthetic-stop", startInfo.Environment["SCRCPY_STOP_EVENT"]);
            Assert.Equal("synthetic.example:37123", startInfo.Environment["SCRCPY_RECONNECT_SERIAL"]);
            Assert.False(startInfo.Environment.TryGetValue("ADB_MDNS_OPENSCREEN", out string? backend) && backend == "1");
            Assert.False(Directory.Exists(Path.Combine(directory, "sessions")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Legacy reconnect is activated only by an explicit endpoint in the immutable request.</summary>
    [Theory]
    [InlineData(false, "synthetic.example:37123")]
    [InlineData(true, null)]
    public void StartInfoDoesNotInferReconnectEndpoint(bool reconnect, string? endpoint)
    {
        string directory = CreateDirectory();

        try
        {
            LegacyNativeHost host = new(
                CreateFile(directory, "scrcpy.exe"),
                CreateFile(directory, "scrcpy-server"),
                CreateFile(directory, "adb.exe"),
                Path.Combine(directory, "sessions"));
            NativeStartRequest request = CreateRequest("SYNTHETIC_USB", endpoint is null ? null : NetworkEndpoint.Parse(endpoint),
                reconnect: reconnect);
            ProcessStartInfo startInfo = host.CreateStartInfo(request, "Local\\synthetic-stop");

            Assert.False(startInfo.Environment.ContainsKey("SCRCPY_RECONNECT_SERIAL"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Missing runtime components fail before creating a session directory or child.</summary>
    [Fact]
    public void MissingBundleComponentFailsPreflight()
    {
        string directory = CreateDirectory();

        try
        {
            string nativePath = CreateFile(directory, "scrcpy.exe");
            string adbPath = CreateFile(directory, "adb.exe");
            string sessionDirectory = Path.Combine(directory, "sessions");

            Assert.Throws<FileNotFoundException>(() => new LegacyNativeHost(
                nativePath,
                Path.Combine(directory, "missing-server"),
                adbPath,
                sessionDirectory));
            Assert.False(Directory.Exists(sessionDirectory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Natural child exit settles once and does not trigger an automatic replacement.</summary>
    [Fact]
    public async Task ImmediateChildExitCompletesWithoutRestart()
    {
        using EventWaitHandle stopEvent = new(false, EventResetMode.ManualReset);
        ProcessStartInfo startInfo = CreatePowerShellStartInfo("[Console]::Out.Write('done')");
        await using LegacyNativeSession session = LegacyNativeSession.Start(SessionId.New(), startInfo, stopEvent);

        NativeExit result = await session.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(NativeTerminationReason.WindowClosed, result.Reason);
        Assert.Equal("done", session.StandardOutput);
        Assert.False(session.WasForced);
        Assert.True(session.ProcessId > 0);
        Assert.Equal(nint.Zero, session.MainWindowHandle);
    }

    /// <summary>Large simultaneous pipes continue draining after bounded retention is full.</summary>
    [Fact]
    public async Task LargeOutputCannotBlockCompletion()
    {
        using EventWaitHandle stopEvent = new(false, EventResetMode.ManualReset);
        ProcessStartInfo startInfo = CreatePowerShellStartInfo(
            "[Console]::Out.Write(('O' * 131072)); [Console]::Error.Write(('E' * 131072))");
        await using LegacyNativeSession session = LegacyNativeSession.Start(SessionId.New(), startInfo, stopEvent);

        NativeExit result = await session.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(NativeTerminationReason.WindowClosed, result.Reason);
        Assert.Equal(32_768, session.StandardOutput.Length);
        Assert.Equal(32_768, session.StandardError.Length);
        Assert.True(session.OutputTruncated);
    }

    /// <summary>Local lifecycle evidence is bounded and excludes raw native output.</summary>
    [Fact]
    public async Task LifecycleTraceContainsOnlyOrderedOwnedProcessEvents()
    {
        string directory = CreateDirectory();
        SessionId sessionId = SessionId.New();
        using EventWaitHandle stopEvent = new(false, EventResetMode.ManualReset);
        using LegacyNativeLifecycleLog log = new(directory, sessionId);
        ProcessStartInfo startInfo = CreatePowerShellStartInfo("[Console]::Out.Write('synthetic-secret-marker')");

        try
        {
            await using (LegacyNativeSession session = LegacyNativeSession.Start(sessionId, startInfo, stopEvent, log))
            {
                await session.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                Assert.Equal(log.Path, session.LifecycleLogPath);
                Assert.Null(session.LifecycleLogError);
            }

            string trace = await File.ReadAllTextAsync(log.Path, TestContext.Current.CancellationToken);
            string[] lines = trace.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, lines.Length);
            Assert.DoesNotContain("synthetic-secret-marker", trace, StringComparison.Ordinal);
            Assert.True(new FileInfo(log.Path).Length <= 8_192);

            using JsonDocument started = JsonDocument.Parse(lines[0]);
            using JsonDocument exited = JsonDocument.Parse(lines[1]);
            Assert.Equal(sessionId.ToString(), started.RootElement.GetProperty("sessionId").GetString());
            Assert.Equal("started", started.RootElement.GetProperty("eventType").GetString());
            Assert.Equal(1, started.RootElement.GetProperty("sequence").GetInt32());
            Assert.Equal("exited", exited.RootElement.GetProperty("eventType").GetString());
            Assert.Equal(2, exited.RootElement.GetProperty("sequence").GetInt32());
            Assert.Equal(started.RootElement.GetProperty("processId").GetInt32(),
                exited.RootElement.GetProperty("processId").GetInt32());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Repeated stop signals the named event and waits for the same exact child.</summary>
    [Fact]
    public async Task StopTwiceGracefullyReapsOnlyOwnedChild()
    {
        string directory = CreateDirectory();
        string readyPath = Path.Combine(directory, "ready.txt");
        string scriptPath = Path.Combine(directory, "stop-child.ps1");
        string stopName = "Local\\synthetic-native-stop-" + Guid.NewGuid().ToString("N");
        using EventWaitHandle stopEvent = new(false, EventResetMode.ManualReset, stopName);
        File.WriteAllText(scriptPath, """
            param([string]$readyPath, [string]$stopName)
            $stopEvent = [Threading.EventWaitHandle]::OpenExisting($stopName)
            [IO.File]::WriteAllText($readyPath, $PID.ToString())
            $stopEvent.WaitOne() | Out-Null
            [Environment]::Exit(0)
            """);
        ProcessStartInfo startInfo = CreatePowerShellFileStartInfo(scriptPath, readyPath, stopName);
        await using LegacyNativeSession session = LegacyNativeSession.Start(SessionId.New(), startInfo, stopEvent);

        try
        {
            await WaitForFileAsync(readyPath, session.Completion);
            int ownedProcessId = int.Parse(await File.ReadAllTextAsync(readyPath, TestContext.Current.CancellationToken));
            await Task.WhenAll(
                session.StopAsync(NativeTerminationReason.UserStop, CancellationToken.None),
                session.StopAsync(NativeTerminationReason.UserStop, CancellationToken.None));

            NativeExit result = await session.Completion;
            Assert.Equal(ownedProcessId, session.ProcessId);
            Assert.Equal(NativeTerminationReason.UserStop, result.Reason);
            Assert.False(session.WasForced);
            Assert.False(IsProcessAlive(ownedProcessId));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Escalation is explicit and cannot terminate an unrelated synthetic process.</summary>
    [Fact]
    public async Task ForcedStopKillsOnlyTheOwnedNativeChild()
    {
        string directory = CreateDirectory();
        string scriptPath = Path.Combine(directory, "blocking-child.ps1");
        string ownedMarker = Path.Combine(directory, "owned-ready.txt");
        string unrelatedMarker = Path.Combine(directory, "unrelated-ready.txt");
        File.WriteAllText(scriptPath, """
            param([string]$readyPath)
            [IO.File]::WriteAllText($readyPath, $PID.ToString())
            [Threading.ManualResetEventSlim]::new($false).Wait()
            """);
        using EventWaitHandle stopEvent = new(false, EventResetMode.ManualReset);
        using Process unrelated = new()
        {
            StartInfo = CreatePowerShellFileStartInfo(scriptPath, unrelatedMarker),
        };
        Assert.True(unrelated.Start());
        LegacyNativeSession? session = null;

        try
        {
            session = LegacyNativeSession.Start(SessionId.New(),
                CreatePowerShellFileStartInfo(scriptPath, ownedMarker), stopEvent);
            await WaitForFileAsync(ownedMarker, session.Completion);
            await WaitForFileAsync(unrelatedMarker, unrelated.WaitForExitAsync(TestContext.Current.CancellationToken));
            int ownedProcessId = int.Parse(await File.ReadAllTextAsync(ownedMarker, TestContext.Current.CancellationToken));
            int unrelatedProcessId = int.Parse(await File.ReadAllTextAsync(unrelatedMarker, TestContext.Current.CancellationToken));

            await session.StopAsync(NativeTerminationReason.UserStop, TestContext.Current.CancellationToken);

            Assert.False(IsProcessAlive(ownedProcessId));
            Assert.True(IsProcessAlive(unrelatedProcessId));
            Assert.True(session.WasForced);
            Assert.Equal(NativeTerminationReason.NativeFailure, (await session.Completion).Reason);
        }
        finally
        {
            if (session is not null)
            {
                await session.DisposeAsync();
            }

            if (!unrelated.HasExited)
            {
                unrelated.Kill();
                await unrelated.WaitForExitAsync(TestContext.Current.CancellationToken);
            }

            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Creates a synthetic request with no real phone identity.</summary>
    private static NativeStartRequest CreateRequest(
        string selectedAdbSerial,
        NetworkEndpoint? endpoint,
        Dictionary<string, JsonElement>? options = null,
        bool reconnect = true)
    {
        DeviceProfile profile = new()
        {
            Id = ProfileId.New(),
            UsbIdentity = new UsbSerial("SYNTHETIC_USB"),
        };
        Assert.True(ConnectionPlan.TryCreate(profile, ConnectionPolicy.Default, out ConnectionPlan? plan, out _));
        return new NativeStartRequest(SessionId.New(), plan!, new MirroringPreferences
        {
            Reconnect = reconnect,
            Options = options ?? [],
        }, selectedAdbSerial, TransportKind.Usb, endpoint, "synthetic-revision");
    }

    /// <summary>Builds a Windows-only synthetic process invocation with bounded redirected pipes.</summary>
    private static ProcessStartInfo CreatePowerShellStartInfo(string script, params string[] arguments)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(script);

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    /// <summary>Passes paths and event names as discrete PowerShell script parameters.</summary>
    private static ProcessStartInfo CreatePowerShellFileStartInfo(string scriptPath, params string[] arguments)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    /// <summary>Waits for a child-created marker without relying on a sleep duration for correctness.</summary>
    private static async Task WaitForFileAsync(string path, Task completion)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(20));
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));

        while (!File.Exists(path))
        {
            if (completion.IsCompleted)
            {
                await completion;
                throw new Xunit.Sdk.XunitException("The synthetic child exited before creating its marker.");
            }

            Assert.True(await timer.WaitForNextTickAsync(timeout.Token));
        }
    }

    /// <summary>Checks only the exact PID created by this test.</summary>
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

    /// <summary>Creates an isolated path containing spaces for argument validation.</summary>
    private static string CreateDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "scrcpy seamless native test " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Creates an inert file; translation never executes it.</summary>
    private static string CreateFile(string directory, string fileName)
    {
        string path = Path.Combine(directory, fileName);
        File.WriteAllText(path, "synthetic");
        return path;
    }

}
