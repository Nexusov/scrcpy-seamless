using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Adb;
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
            Assert.Equal(Environment.GetEnvironmentVariable("ADB_MDNS_OPENSCREEN"),
                startInfo.Environment.TryGetValue("ADB_MDNS_OPENSCREEN", out string? backend) ? backend : null);
            Assert.False(Directory.Exists(Path.Combine(directory, "sessions")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The selected runtime may pass Openscreen only to its native child.</summary>
    [Fact]
    public void ExplicitMdnsCompatibilityIsProcessLocal()
    {
        string directory = CreateDirectory();

        try
        {
            string? previousBackend = Environment.GetEnvironmentVariable("ADB_MDNS_OPENSCREEN");
            LegacyNativeHost host = new(
                CreateFile(directory, "scrcpy.exe"),
                CreateFile(directory, "scrcpy-server"),
                CreateFile(directory, "adb.exe"),
                Path.Combine(directory, "sessions"),
                enableOpenScreenMdnsCompatibility: true);
            ProcessStartInfo startInfo = host.CreateStartInfo(CreateRequest("SYNTHETIC_USB", null),
                "Local\\synthetic-stop");

            Assert.Equal("1", startInfo.Environment["ADB_MDNS_OPENSCREEN"]);
            Assert.Equal(previousBackend, Environment.GetEnvironmentVariable("ADB_MDNS_OPENSCREEN"));
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

    /// <summary>A forbidden USB fallback cannot be restored by inherited legacy environment.</summary>
    [Fact]
    public void DisabledUsbFallbackClearsInheritedReconnectEnvironment()
    {
        const string reconnectVariable = "SCRCPY_RECONNECT_SERIAL";
        string? originalTarget = Environment.GetEnvironmentVariable(reconnectVariable);
        string directory = CreateDirectory();

        try
        {
            Environment.SetEnvironmentVariable(reconnectVariable, "inherited.example:37123");
            DeviceProfile profile = new()
            {
                Id = ProfileId.New(),
                UsbIdentity = new UsbSerial("SYNTHETIC_USB"),
                ConnectionEndpoint = NetworkEndpoint.Parse("synthetic.example:37123"),
                Connection = new ConnectionPreferences
                {
                    PreferredTransport = TransportPreference.Usb,
                    AllowFallback = false,
                },
            };
            ConfigurationV2 configuration = new()
            {
                Profiles = [profile],
                Mirroring = new MirroringPreferences { Reconnect = true },
            };
            NativeLaunchPreparation preparation = NativeLaunchPreflight.Prepare(configuration, "synthetic-revision",
                profile.Id, new AdbDevice("SYNTHETIC_USB", AdbDeviceState.Device, null), SessionId.New());
            Assert.True(preparation.IsReady);

            LegacyNativeHost host = new(
                CreateFile(directory, "scrcpy.exe"),
                CreateFile(directory, "scrcpy-server"),
                CreateFile(directory, "adb.exe"),
                Path.Combine(directory, "sessions"));
            ProcessStartInfo startInfo = host.CreateStartInfo(preparation.Request!, "Local\\synthetic-stop");

            Assert.False(startInfo.Environment.ContainsKey(reconnectVariable));
            Assert.Equal("inherited.example:37123", Environment.GetEnvironmentVariable(reconnectVariable));
        }
        finally
        {
            Environment.SetEnvironmentVariable(reconnectVariable, originalTarget);
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
        string scriptPath = CreateStopChildScript(directory);
        string stopName = "Local\\synthetic-native-stop-" + Guid.NewGuid().ToString("N");
        using EventWaitHandle stopEvent = new(false, EventResetMode.ManualReset, stopName);
        ProcessStartInfo startInfo = CreatePowerShellFileStartInfo(scriptPath, readyPath, stopName);

        await RunStopFixtureAsync(directory, startInfo, stopEvent, async session =>
        {
            await WaitForFileAsync(readyPath, session.Completion, TestContext.Current.CancellationToken);
            int ownedProcessId = int.Parse(await File.ReadAllTextAsync(readyPath, TestContext.Current.CancellationToken));
            await Task.WhenAll(
                session.StopAsync(NativeTerminationReason.UserStop, CancellationToken.None),
                session.StopAsync(NativeTerminationReason.UserStop, CancellationToken.None));

            NativeExit result = await session.Completion;
            Assert.Equal(ownedProcessId, session.ProcessId);
            Assert.Equal(NativeTerminationReason.UserStop, result.Reason);
            Assert.False(session.WasForced);
            Assert.False(IsProcessAlive(ownedProcessId));
            await session.DisposeAsync();
            await session.DisposeAsync();
        });
    }

    /// <summary>A pre-Stop assertion failure must settle the exact child before deleting its fixture.</summary>
    [Fact]
    public async Task EarlyStopFixtureFailureDisposesChildBeforeDeletingDirectory()
    {
        string directory = CreateDirectory();
        string readyPath = Path.Combine(directory, "ready.txt");
        string stopName = "Local\\synthetic-native-stop-" + Guid.NewGuid().ToString("N");
        using EventWaitHandle stopEvent = new(false, EventResetMode.ManualReset, stopName);
        ProcessStartInfo startInfo = CreatePowerShellFileStartInfo(
            CreateStopChildScript(directory), readyPath, stopName);
        int ownedProcessId = 0;

        try
        {
            InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                RunStopFixtureAsync(directory, startInfo, stopEvent, async session =>
                {
                    ownedProcessId = session.ProcessId;
                    await WaitForFileAsync(readyPath, session.Completion, TestContext.Current.CancellationToken);
                    throw new InvalidOperationException("synthetic primary failure");
                }, path =>
                {
                    Assert.False(IsProcessAlive(ownedProcessId));
                    Directory.Delete(path, recursive: true);
                }));

            Assert.Equal("synthetic primary failure", failure.Message);
            Assert.False(IsProcessAlive(ownedProcessId));
            Assert.False(Directory.Exists(directory));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>Reports both a primary assertion failure and a later fixture-delete failure.</summary>
    [Fact]
    public async Task StopFixturePreservesPrimaryAndCleanupFailures()
    {
        string directory = CreateDirectory();
        string readyPath = Path.Combine(directory, "ready.txt");
        string stopName = "Local\\synthetic-native-stop-" + Guid.NewGuid().ToString("N");
        using EventWaitHandle stopEvent = new(false, EventResetMode.ManualReset, stopName);
        ProcessStartInfo startInfo = CreatePowerShellFileStartInfo(
            CreateStopChildScript(directory), readyPath, stopName);
        int ownedProcessId = 0;

        try
        {
            AggregateException failure = await Assert.ThrowsAsync<AggregateException>(() =>
                RunStopFixtureAsync(directory, startInfo, stopEvent, async session =>
                {
                    ownedProcessId = session.ProcessId;
                    await WaitForFileAsync(readyPath, session.Completion, TestContext.Current.CancellationToken);
                    throw new InvalidOperationException("synthetic primary failure");
                }, _ => throw new IOException("synthetic cleanup failure")));

            Assert.Collection(failure.InnerExceptions,
                primary =>
                {
                    Assert.Equal("synthetic primary failure", primary.Message);
                    Assert.NotNull(primary.StackTrace);
                },
                cleanup => Assert.Equal("synthetic cleanup failure", cleanup.Message));
            Assert.False(IsProcessAlive(ownedProcessId));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>An early synthetic child exit gives a bounded readiness failure and no leaked child.</summary>
    [Fact]
    public async Task StopFixtureReportsChildExitBeforeReadiness()
    {
        string directory = CreateDirectory();
        string readyPath = Path.Combine(directory, "ready.txt");
        using EventWaitHandle stopEvent = new(false, EventResetMode.ManualReset);
        int ownedProcessId = 0;

        await RunStopFixtureAsync(directory,
            CreatePowerShellStartInfo("[Environment]::Exit(0)"), stopEvent, async session =>
            {
                ownedProcessId = session.ProcessId;
                Xunit.Sdk.XunitException failure = await Assert.ThrowsAsync<Xunit.Sdk.XunitException>(() =>
                    WaitForFileAsync(readyPath, session.Completion, TestContext.Current.CancellationToken));
                Assert.Contains("exited before creating its marker", failure.Message, StringComparison.Ordinal);
            });

        Assert.False(IsProcessAlive(ownedProcessId));
        Assert.False(Directory.Exists(directory));
    }

    /// <summary>Cancellation of a readiness wait still closes the exact owned child.</summary>
    [Fact]
    public async Task StopFixtureCleansUpWhenReadinessIsCancelled()
    {
        string directory = CreateDirectory();
        string readyPath = Path.Combine(directory, "ready.txt");
        string scriptPath = Path.Combine(directory, "wait-for-stop.ps1");
        string stopName = "Local\\synthetic-native-stop-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(scriptPath, """
            param([string]$stopName)
            $stopEvent = [Threading.EventWaitHandle]::OpenExisting($stopName)
            $stopEvent.WaitOne() | Out-Null
            """);
        using EventWaitHandle stopEvent = new(false, EventResetMode.ManualReset, stopName);
        using CancellationTokenSource cancelledReadiness = new();
        int ownedProcessId = 0;

        await RunStopFixtureAsync(directory,
            CreatePowerShellFileStartInfo(scriptPath, stopName), stopEvent, async session =>
            {
                ownedProcessId = session.ProcessId;
                Task readiness = WaitForFileAsync(readyPath, session.Completion, cancelledReadiness.Token);
                Assert.False(readiness.IsCompleted);
                cancelledReadiness.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => readiness);
            });

        Assert.False(IsProcessAlive(ownedProcessId));
        Assert.False(Directory.Exists(directory));
    }

    /// <summary>A visible marker is not ready while its synthetic writer still holds the file.</summary>
    [Fact]
    public async Task ReadyMarkerWaitsForClosedPublication()
    {
        string directory = CreateDirectory();
        string readyPath = Path.Combine(directory, "ready.txt");
        string scriptPath = CreateStopChildScript(directory);
        string stopName = "Local\\synthetic-native-stop-" + Guid.NewGuid().ToString("N");
        string openedName = "Local\\synthetic-marker-opened-" + Guid.NewGuid().ToString("N");
        string releaseName = "Local\\synthetic-marker-release-" + Guid.NewGuid().ToString("N");
        using EventWaitHandle stopEvent = new(false, EventResetMode.ManualReset, stopName);
        using EventWaitHandle markerOpened = new(false, EventResetMode.ManualReset, openedName);
        using EventWaitHandle releaseMarker = new(false, EventResetMode.ManualReset, releaseName);
        ProcessStartInfo startInfo = CreatePowerShellFileStartInfo(
            scriptPath, readyPath, stopName, openedName, releaseName);

        await RunStopFixtureAsync(directory, startInfo, stopEvent, async session =>
        {
            try
            {
                Assert.True(await Task.Run(() => markerOpened.WaitOne(TimeSpan.FromSeconds(10))));
                Assert.True(File.Exists(readyPath + ".tmp"));
                Assert.False(File.Exists(readyPath));
                Task readiness = WaitForFileAsync(
                    readyPath, session.Completion, TestContext.Current.CancellationToken);
                Assert.False(readiness.IsCompleted);

                releaseMarker.Set();
                await readiness;
                Assert.Equal(session.ProcessId.ToString(),
                    await File.ReadAllTextAsync(readyPath, TestContext.Current.CancellationToken));
            }
            finally
            {
                releaseMarker.Set();
            }
        });
    }

    /// <summary>Shows why the former final-name existence check did not prove a closed write.</summary>
    [Fact]
    public void OpenFinalMarkerIsVisibleBeforeItsWriterCloses()
    {
        string directory = CreateDirectory();
        string readyPath = Path.Combine(directory, "ready.txt");

        try
        {
            using (FileStream writer = new(readyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                writer.WriteByte((byte)'4');
                Assert.True(File.Exists(readyPath));
                Assert.Throws<IOException>(() => File.ReadAllText(readyPath));
            }

            Assert.Equal("4", File.ReadAllText(readyPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A failed stop attempt does not permanently poison cleanup of the same child.</summary>
    [Fact]
    public async Task FailedStopCanBeRetriedAfterTheOwnedChildExits()
    {
        string directory = CreateDirectory();
        string readyPath = Path.Combine(directory, "ready.txt");
        string scriptPath = Path.Combine(directory, "blocking-child.ps1");
        File.WriteAllText(scriptPath, """
            param([string]$readyPath)
            [IO.File]::WriteAllText($readyPath, $PID.ToString())
            [Threading.ManualResetEventSlim]::new($false).Wait()
            """);
        using EventWaitHandle stopEvent = new(false, EventResetMode.ManualReset);
        LegacyNativeSession session = LegacyNativeSession.Start(SessionId.New(),
            CreatePowerShellFileStartInfo(scriptPath, readyPath), stopEvent);

        try
        {
            await WaitForFileAsync(readyPath, session.Completion, TestContext.Current.CancellationToken);
            stopEvent.Dispose();
            await Assert.ThrowsAsync<ObjectDisposedException>(() =>
                session.StopAsync(NativeTerminationReason.UserStop, CancellationToken.None));
            Assert.True(IsProcessAlive(session.ProcessId));

            using Process owned = Process.GetProcessById(session.ProcessId);
            owned.Kill();
            await owned.WaitForExitAsync(TestContext.Current.CancellationToken);
            await session.Completion;

            await session.StopAsync(NativeTerminationReason.UserStop, CancellationToken.None);
            await session.DisposeAsync();
            Assert.False(IsProcessAlive(session.ProcessId));
        }
        finally
        {
            if (IsProcessAlive(session.ProcessId))
            {
                using Process owned = Process.GetProcessById(session.ProcessId);
                owned.Kill();
                await owned.WaitForExitAsync(TestContext.Current.CancellationToken);
            }

            await session.DisposeAsync();
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
            await WaitForFileAsync(ownedMarker, session.Completion, TestContext.Current.CancellationToken);
            await WaitForFileAsync(unrelatedMarker,
                unrelated.WaitForExitAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
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

    /// <summary>Creates the shared synthetic child script used by readiness and graceful-stop checks.</summary>
    private static string CreateStopChildScript(string directory)
    {
        string scriptPath = Path.Combine(directory, "stop-child.ps1");
        File.WriteAllText(scriptPath, """
            param([string]$readyPath, [string]$stopName, [string]$openedName, [string]$releaseName)
            $temporaryPath = $readyPath + '.tmp'
            $writer = [IO.File]::Open($temporaryPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            try {
                $bytes = [Text.Encoding]::UTF8.GetBytes($PID.ToString())
                $writer.Write($bytes, 0, $bytes.Length)
                if ($openedName) {
                    $opened = [Threading.EventWaitHandle]::OpenExisting($openedName)
                    $opened.Set() | Out-Null
                    $release = [Threading.EventWaitHandle]::OpenExisting($releaseName)
                    $release.WaitOne() | Out-Null
                }
            } finally {
                $writer.Dispose()
            }
            [IO.File]::Move($temporaryPath, $readyPath)
            $stopEvent = [Threading.EventWaitHandle]::OpenExisting($stopName)
            $stopEvent.WaitOne() | Out-Null
            [Environment]::Exit(0)
            """);
        return scriptPath;
    }

    /// <summary>Runs the real session and owns its synthetic fixture directory.</summary>
    private static async Task RunStopFixtureAsync(
        string directory,
        ProcessStartInfo startInfo,
        EventWaitHandle stopEvent,
        Func<LegacyNativeSession, Task> exercise,
        Action<string>? deleteDirectory = null)
    {
        LegacyNativeSession? session = null;
        Process? ownedChild = null;
        Exception? primaryFailure = null;
        List<Exception> cleanupFailures = [];

        try
        {
            session = LegacyNativeSession.Start(SessionId.New(), startInfo, stopEvent);

            try
            {
                ownedChild = Process.GetProcessById(session.ProcessId);
                _ = ownedChild.Handle;
            }
            catch (ArgumentException)
            {
                ownedChild?.Dispose();
                ownedChild = null;
            }
            catch (InvalidOperationException) when (!IsProcessAlive(session.ProcessId))
            {
                ownedChild?.Dispose();
                ownedChild = null;
            }

            await exercise(session);
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
        }

        bool sessionDisposed = session is null;

        if (session is not null)
        {
            try
            {
                await session.DisposeAsync();
                sessionDisposed = true;
            }
            catch (Exception exception)
            {
                cleanupFailures.Add(exception);

                if (ownedChild is not null && !ownedChild.HasExited)
                {
                    try
                    {
                        ownedChild.Kill();
                        await ownedChild.WaitForExitAsync(CancellationToken.None)
                            .WaitAsync(TimeSpan.FromSeconds(5));
                        await session.Completion;
                    }
                    catch (Exception terminationException)
                    {
                        cleanupFailures.Add(terminationException);
                    }
                }

                if (ownedChild is null || ownedChild.HasExited)
                {
                    try
                    {
                        await session.DisposeAsync();
                        sessionDisposed = true;
                    }
                    catch (Exception disposalRetryException)
                    {
                        cleanupFailures.Add(disposalRetryException);
                    }
                }
            }
        }

        bool ownedChildSettled = sessionDisposed && (ownedChild is null || ownedChild.HasExited);
        ownedChild?.Dispose();

        if (ownedChildSettled)
        {
            try
            {
                (deleteDirectory ?? (path => Directory.Delete(path, recursive: true)))(directory);
            }
            catch (Exception exception)
            {
                cleanupFailures.Add(exception);
            }
        }
        else
        {
            cleanupFailures.Add(new InvalidOperationException(
                "The owned fixture child is still alive; the fixture directory was not deleted."));
        }

        if (primaryFailure is not null && cleanupFailures.Count == 0)
        {
            ExceptionDispatchInfo.Capture(primaryFailure).Throw();
        }

        if (primaryFailure is null && cleanupFailures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(cleanupFailures[0]).Throw();
        }

        if (primaryFailure is not null || cleanupFailures.Count > 0)
        {
            List<Exception> failures = primaryFailure is null ? [] : [primaryFailure];
            failures.AddRange(cleanupFailures);
            throw new AggregateException("The native fixture and its cleanup both failed.", failures);
        }
    }

    /// <summary>Waits for a child-created marker without relying on a sleep duration for correctness.</summary>
    private static async Task WaitForFileAsync(
        string path, Task completion, CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(20));
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

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
