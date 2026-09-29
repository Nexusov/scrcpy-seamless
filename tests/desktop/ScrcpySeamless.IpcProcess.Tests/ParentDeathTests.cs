using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace ScrcpySeamless.IpcProcess.Tests;

/// <summary>Exercises native parent-death protection through the production managed host and real pipes.</summary>
public sealed class ParentDeathTests
{
    private static readonly TimeSpan StartupLimit = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ParentDeathLimit = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CleanupLimit = TimeSpan.FromSeconds(10);

    /// <summary>Killing the exact host process ends only its mirror child, including a child with a descendant.</summary>
    [Fact]
    public async Task KilledSupervisorEndsNativeChildButNotDaemonLikeDescendant()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The parent-death test uses Windows process handles.");
        }

        string supervisorExecutable = RequireExecutable("SCRCPY_IPC_SUPERVISOR_FIXTURE");
        string nativeExecutable = RequireExecutable("SCRCPY_IPC_NATIVE_TEST_CHILD");
        string testRoot = RequireDirectory("SCRCPY_IPC_TEST_ROOT");
        string fixtureDirectory = Path.Combine(testRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureDirectory);

        // Named synchronization is passed as names, never as inheritable handles.
        string supervisorReadyName = EventName("supervisor");
        string daemonReadyName = EventName("daemon");
        string daemonReleaseName = EventName("daemon-release");
        string unrelatedReadyName = EventName("unrelated");
        string unrelatedReleaseName = EventName("unrelated-release");
        using EventWaitHandle supervisorReadyNamed = CreateEvent(supervisorReadyName);
        using EventWaitHandle daemonReadyNamed = CreateEvent(daemonReadyName);
        using EventWaitHandle daemonReleaseNamed = CreateEvent(daemonReleaseName);
        using EventWaitHandle unrelatedReadyNamed = CreateEvent(unrelatedReadyName);
        using EventWaitHandle unrelatedReleaseNamed = CreateEvent(unrelatedReleaseName);
        using Process supervisor = new()
        {
            StartInfo = CreateStartInfo(supervisorExecutable, nativeExecutable, fixtureDirectory,
                supervisorReadyName, daemonReadyName, daemonReleaseName),
        };
        Process? native = null;
        Process? daemon = null;
        Process? unrelated = null;
        bool supervisorStarted = false;
        bool unrelatedStarted = false;
        Exception? primaryFailure = null;
        List<Exception> cleanupFailures = [];

        try
        {
            supervisorStarted = supervisor.Start();
            Assert.True(supervisorStarted);
            await WaitForSignalAsync(supervisorReadyNamed, "supervisor readiness", StartupLimit);
            SupervisorIdentity identity = await ReadJsonAsync<SupervisorIdentity>(
                Path.Combine(fixtureDirectory, "supervisor-ready.json"));
            Assert.Equal(supervisor.Id, identity.SupervisorPid);
            native = OpenExactProcess(identity.NativePid, identity.NativeCreatedFileTime);
            Assert.False(native.HasExited);

            await WaitForSignalAsync(daemonReadyNamed, "daemon readiness", StartupLimit);
            DaemonIdentity descendant = await ReadJsonAsync<DaemonIdentity>(
                Path.Combine(fixtureDirectory, "daemon-ready.json"));
            daemon = OpenExactProcess(descendant.Pid, checked((long)descendant.CreatedFileTime));
            Assert.False(daemon.HasExited);

            unrelated = new Process
            {
                StartInfo = CreateStartInfo(supervisorExecutable, "--unrelated",
                    unrelatedReadyName, unrelatedReleaseName),
            };
            unrelatedStarted = unrelated.Start();
            Assert.True(unrelatedStarted);
            await WaitForSignalAsync(unrelatedReadyNamed, "unrelated readiness", StartupLimit);
            Assert.False(unrelated.HasExited);

            supervisor.Kill();
            await supervisor.WaitForExitAsync(TestContext.Current.CancellationToken)
                .WaitAsync(ParentDeathLimit, TestContext.Current.CancellationToken);
            await native.WaitForExitAsync(TestContext.Current.CancellationToken)
                .WaitAsync(ParentDeathLimit, TestContext.Current.CancellationToken);

            Assert.True(native.HasExited);
            Assert.True(daemon is { HasExited: false });
            Assert.True(unrelated is { HasExited: false });
        }
        catch (Exception exception)
        {
            string supervisorError = supervisorStarted && supervisor.HasExited
                ? await supervisor.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken)
                : "Supervisor still running.";
            primaryFailure = new InvalidOperationException(
                $"Parent-death fixture failed. Supervisor diagnostics: {supervisorError}", exception);
        }

        if (supervisorStarted)
        {
            await SettleProcessAsync(supervisor, cleanupFailures);
        }

        await SettleProcessAsync(native, cleanupFailures);
        daemonReleaseNamed.Set();
        unrelatedReleaseNamed.Set();
        await SettleProcessAsync(daemon, cleanupFailures);
        if (unrelatedStarted)
        {
            await SettleProcessAsync(unrelated, cleanupFailures);
        }
        native?.Dispose();
        daemon?.Dispose();
        unrelated?.Dispose();

        if (cleanupFailures.Count == 0)
        {
            try
            {
                Directory.Delete(fixtureDirectory, recursive: true);
            }
            catch (Exception exception)
            {
                cleanupFailures.Add(exception);
            }
        }

        if (primaryFailure is not null && cleanupFailures.Count > 0)
        {
            throw new AggregateException([primaryFailure, .. cleanupFailures]);
        }

        if (primaryFailure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primaryFailure).Throw();
        }

        if (cleanupFailures.Count > 0)
        {
            throw new AggregateException(cleanupFailures);
        }
    }

    /// <summary>A parent lost before guard installation cannot authorize handshake or synthetic work.</summary>
    [Fact]
    public async Task ParentDeathBeforeGuardInstallationRejectsStaleBootstrap()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The parent-death test uses Windows process handles.");
        }

        string supervisorExecutable = RequireExecutable("SCRCPY_IPC_SUPERVISOR_FIXTURE");
        string nativeExecutable = RequireExecutable("SCRCPY_IPC_NATIVE_TEST_CHILD");
        string fixtureDirectory = Path.Combine(RequireDirectory("SCRCPY_IPC_TEST_ROOT"),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureDirectory);
        string readyName = EventName("pre-guard-ready");
        string releaseName = EventName("pre-guard-release");
        using EventWaitHandle ready = CreateEvent(readyName);
        using EventWaitHandle release = CreateEvent(releaseName);
        using Process supervisor = new()
        {
            StartInfo = CreateStartInfo(supervisorExecutable, "--early-parent-death",
                nativeExecutable, fixtureDirectory, readyName, releaseName),
        };
        Process? native = null;
        bool supervisorStarted = false;
        Exception? primaryFailure = null;
        List<Exception> cleanupFailures = [];

        try
        {
            supervisorStarted = supervisor.Start();
            Assert.True(supervisorStarted);
            await WaitForSignalAsync(ready, "pre-guard publication", StartupLimit);
            DaemonIdentity child = await ReadJsonAsync<DaemonIdentity>(
                Path.Combine(fixtureDirectory, "pre-guard-ready.json"));
            native = OpenExactProcess(child.Pid, checked((long)child.CreatedFileTime));
            Assert.False(native.HasExited);

            supervisor.Kill();
            await supervisor.WaitForExitAsync(TestContext.Current.CancellationToken)
                .WaitAsync(ParentDeathLimit, TestContext.Current.CancellationToken);
            release.Set();
            await native.WaitForExitAsync(TestContext.Current.CancellationToken)
                .WaitAsync(ParentDeathLimit, TestContext.Current.CancellationToken);

            Assert.NotEqual(0, native.ExitCode);
            Assert.False(File.Exists(Path.Combine(fixtureDirectory, "daemon-ready.json")));
        }
        catch (Exception exception)
        {
            string supervisorError = supervisorStarted && supervisor.HasExited
                ? await supervisor.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken)
                : "Supervisor still running.";
            primaryFailure = new InvalidOperationException(
                $"Pre-guard fixture failed. Supervisor diagnostics: {supervisorError}", exception);
        }

        release.Set();
        if (supervisorStarted)
        {
            await SettleProcessAsync(supervisor, cleanupFailures);
        }

        await SettleProcessAsync(native, cleanupFailures);
        native?.Dispose();
        if (cleanupFailures.Count == 0)
        {
            try
            {
                Directory.Delete(fixtureDirectory, recursive: true);
            }
            catch (Exception exception)
            {
                cleanupFailures.Add(exception);
            }
        }

        if (primaryFailure is not null && cleanupFailures.Count > 0)
        {
            throw new AggregateException([primaryFailure, .. cleanupFailures]);
        }

        if (primaryFailure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primaryFailure).Throw();
        }

        if (cleanupFailures.Count > 0)
        {
            throw new AggregateException(cleanupFailures);
        }
    }

    /// <summary>Real Focus and Stop dispatch settle protocol pipes while a daemon descendant remains alive.</summary>
    [Fact]
    public async Task FocusAndGracefulStopSettleWithoutWaitingForDaemonDescendant()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The process test uses Windows handles.");
        }

        string supervisorExecutable = RequireExecutable("SCRCPY_IPC_SUPERVISOR_FIXTURE");
        string nativeExecutable = RequireExecutable("SCRCPY_IPC_NATIVE_TEST_CHILD");
        string fixtureDirectory = Path.Combine(RequireDirectory("SCRCPY_IPC_TEST_ROOT"),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureDirectory);
        string supervisorReadyName = EventName("graceful-ready");
        string daemonReadyName = EventName("graceful-daemon");
        string daemonReleaseName = EventName("graceful-daemon-release");
        string stopName = EventName("graceful-stop");
        string stoppedName = EventName("graceful-stopped");
        using EventWaitHandle supervisorReady = CreateEvent(supervisorReadyName);
        using EventWaitHandle daemonReady = CreateEvent(daemonReadyName);
        using EventWaitHandle daemonRelease = CreateEvent(daemonReleaseName);
        using EventWaitHandle stop = CreateEvent(stopName);
        using EventWaitHandle stopped = CreateEvent(stoppedName);
        using Process supervisor = new()
        {
            StartInfo = CreateStartInfo(supervisorExecutable, nativeExecutable, fixtureDirectory,
                supervisorReadyName, daemonReadyName, daemonReleaseName),
        };
        supervisor.StartInfo.Environment["SCRCPY_IPC_TEST_SUPERVISOR_STOP_EVENT"] = stopName;
        supervisor.StartInfo.Environment["SCRCPY_IPC_TEST_SUPERVISOR_STOPPED_EVENT"] = stoppedName;
        Process? native = null;
        Process? daemon = null;
        bool supervisorStarted = false;
        Exception? primaryFailure = null;
        List<Exception> cleanupFailures = [];

        try
        {
            supervisorStarted = supervisor.Start();
            Assert.True(supervisorStarted);
            await WaitForSignalAsync(supervisorReady, "graceful supervisor readiness", StartupLimit);
            SupervisorIdentity identity = await ReadJsonAsync<SupervisorIdentity>(
                Path.Combine(fixtureDirectory, "supervisor-ready.json"));
            Assert.Equal(supervisor.Id, identity.SupervisorPid);
            native = OpenExactProcess(identity.NativePid, identity.NativeCreatedFileTime);
            Assert.False(native.HasExited);
            await WaitForSignalAsync(daemonReady, "graceful daemon readiness", StartupLimit);
            DaemonIdentity descendant = await ReadJsonAsync<DaemonIdentity>(
                Path.Combine(fixtureDirectory, "daemon-ready.json"));
            daemon = OpenExactProcess(descendant.Pid, checked((long)descendant.CreatedFileTime));
            Assert.False(daemon.HasExited);

            stop.Set();
            await WaitForSignalAsync(stopped, "graceful child settlement", CleanupLimit);
            StopIdentity result = await ReadJsonAsync<StopIdentity>(
                Path.Combine(fixtureDirectory, "supervisor-stopped.json"));
            Assert.Equal(identity.NativePid, result.NativePid);
            Assert.Equal("UserStop", result.Reason);
            Assert.Equal("NoWindow", result.Focus);
            Assert.True(result.NativeReadyObserved);
            Assert.True(result.SessionStoppedObserved);
            Assert.True(native.HasExited);
            await supervisor.WaitForExitAsync(TestContext.Current.CancellationToken)
                .WaitAsync(CleanupLimit, TestContext.Current.CancellationToken);
            Assert.Equal(0, supervisor.ExitCode);
            Assert.False(daemon.HasExited);
        }
        catch (Exception exception)
        {
            string supervisorError = supervisorStarted && supervisor.HasExited
                ? await supervisor.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken)
                : "Supervisor still running.";
            primaryFailure = new InvalidOperationException(
                $"Graceful fixture failed. Supervisor diagnostics: {supervisorError}", exception);
        }

        stop.Set();
        daemonRelease.Set();
        if (supervisorStarted)
        {
            await SettleProcessAsync(supervisor, cleanupFailures);
        }

        await SettleProcessAsync(native, cleanupFailures);
        await SettleProcessAsync(daemon, cleanupFailures);
        native?.Dispose();
        daemon?.Dispose();
        if (cleanupFailures.Count == 0)
        {
            try
            {
                Directory.Delete(fixtureDirectory, recursive: true);
            }
            catch (Exception exception)
            {
                cleanupFailures.Add(exception);
            }
        }

        if (primaryFailure is not null && cleanupFailures.Count > 0)
        {
            throw new AggregateException([primaryFailure, .. cleanupFailures]);
        }

        if (primaryFailure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primaryFailure).Throw();
        }

        if (cleanupFailures.Count > 0)
        {
            throw new AggregateException(cleanupFailures);
        }
    }

    /// <summary>Creates a named manual-reset event with a test-only identity.</summary>
    private static EventWaitHandle CreateEvent(string name) =>
        new(false, EventResetMode.ManualReset, name);

    /// <summary>Provides an unguessable local event name for one fixture process.</summary>
    private static string EventName(string purpose) =>
        "Local\\scrcpy-seamless-p06b-" + purpose + "-" + Guid.NewGuid().ToString("N");

    /// <summary>Creates an exact executable launch with discrete arguments.</summary>
    private static ProcessStartInfo CreateStartInfo(string executable, params string[] arguments)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    /// <summary>Requires a built disposable process target rather than silently skipping the test.</summary>
    private static string RequireExecutable(string variableName)
    {
        string? path = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException($"Set {variableName} to a built test executable.", path);
        }

        return Path.GetFullPath(path);
    }

    /// <summary>Requires project-local scratch selected by the test runner.</summary>
    private static string RequireDirectory(string variableName)
    {
        string? path = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Set {variableName} to an existing project-local directory.");
        }

        return Path.GetFullPath(path);
    }

    /// <summary>Waits on a semantic barrier with a finite failure bound.</summary>
    private static async Task WaitForSignalAsync(EventWaitHandle ready, string label, TimeSpan limit)
    {
        Assert.True(await Task.Run(() => ready.WaitOne(limit)), $"Timed out waiting for {label}.");
    }

    /// <summary>Reads only a completely published identity marker.</summary>
    private static async Task<T> ReadJsonAsync<T>(string path)
    {
        byte[] bytes = await File.ReadAllBytesAsync(path);
        return JsonSerializer.Deserialize<T>(bytes) ??
            throw new InvalidDataException($"Identity marker was empty: {path}");
    }

    /// <summary>Retains a process handle and rejects a reused numeric PID.</summary>
    private static Process OpenExactProcess(int processId, long createdFileTime)
    {
        Process process = Process.GetProcessById(processId);
        try
        {
            _ = process.Handle;
            Assert.Equal(createdFileTime, process.StartTime.ToFileTimeUtc());
            return process;
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    /// <summary>Settles a known test-created process before fixture files are deleted.</summary>
    private static async Task SettleProcessAsync(Process? process, List<Exception> failures)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                try
                {
                    await process.WaitForExitAsync().WaitAsync(CleanupLimit);
                }
                catch (TimeoutException)
                {
                    process.Kill();
                    await process.WaitForExitAsync().WaitAsync(CleanupLimit);
                }
            }
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    /// <summary>Supervisor-published exact native process identity.</summary>
    private sealed record SupervisorIdentity(int SupervisorPid, int NativePid,
        long NativeCreatedFileTime, string SessionId);

    /// <summary>Native fixture-published daemon descendant identity.</summary>
    private sealed record DaemonIdentity(int Pid, ulong CreatedFileTime);

    /// <summary>Supervisor-published result after graceful command and stream settlement.</summary>
    private sealed record StopIdentity(int NativePid, string Reason, string Focus,
        bool NativeReadyObserved, bool SessionStoppedObserved);
}
