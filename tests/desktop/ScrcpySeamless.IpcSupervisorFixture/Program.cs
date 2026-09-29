using System.Diagnostics;
using System.Text.Json;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Application.Connection;
using ScrcpySeamless.Core.Application.NativeHost;
using ScrcpySeamless.Core.Configuration;
using ScrcpySeamless.Infrastructure.NativeHost;

namespace ScrcpySeamless.IpcSupervisorFixture;

/// <summary>Owns a real machine-session child until the outer test kills this supervisor.</summary>
internal static class Program
{
    private const string FixtureDirectoryVariable = "SCRCPY_IPC_TEST_DIRECTORY";

    /// <summary>Starts the production host and publishes the owned child's identity after handshake.</summary>
    private static async Task<int> Main(string[] arguments)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The parent-death fixture uses Windows process handles.");
        }

        if (arguments is ["--unrelated", string unrelatedReadyName, string unrelatedReleaseName])
        {
            using EventWaitHandle unrelatedReady = EventWaitHandle.OpenExisting(unrelatedReadyName);
            using EventWaitHandle unrelatedRelease = EventWaitHandle.OpenExisting(unrelatedReleaseName);
            unrelatedReady.Set();
            unrelatedRelease.WaitOne();
            return 0;
        }

        if (arguments.Length != 5)
        {
            Console.Error.WriteLine("Expected native executable, fixture directory, and three event names.");
            return 2;
        }

        bool earlyParentDeath = arguments[0] == "--early-parent-death";
        string nativeExecutable = Path.GetFullPath(arguments[earlyParentDeath ? 1 : 0]);
        string fixtureDirectory = Path.GetFullPath(arguments[earlyParentDeath ? 2 : 1]);
        string readinessEventName = arguments[2];
        string daemonReadinessEventName = arguments[3];
        string daemonReleaseEventName = arguments[4];
        Directory.CreateDirectory(fixtureDirectory);

        // This environment is limited to the disposable supervisor and its exact test child.
        Environment.SetEnvironmentVariable(FixtureDirectoryVariable, fixtureDirectory,
            EnvironmentVariableTarget.Process);
        if (earlyParentDeath)
        {
            Environment.SetEnvironmentVariable("SCRCPY_IPC_TEST_PRE_GUARD_READY_EVENT", arguments[3],
                EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("SCRCPY_IPC_TEST_PRE_GUARD_RELEASE_EVENT", arguments[4],
                EnvironmentVariableTarget.Process);
        }
        else
        {
            Environment.SetEnvironmentVariable("SCRCPY_IPC_TEST_DAEMON_READY_EVENT", daemonReadinessEventName,
                EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("SCRCPY_IPC_TEST_DAEMON_RELEASE_EVENT", daemonReleaseEventName,
                EnvironmentVariableTarget.Process);
        }
        string serverPath = Path.Combine(fixtureDirectory, "synthetic-server");
        string adbPath = Path.Combine(fixtureDirectory, "synthetic-adb.exe");
        File.WriteAllBytes(serverPath, []);
        File.WriteAllBytes(adbPath, []);

        DeviceProfile profile = new()
        {
            Id = ProfileId.New(),
            UsbIdentity = new UsbSerial("SYNTHETIC_USB"),
        };
        if (!ConnectionPlan.TryCreate(profile, ConnectionPolicy.Default, out ConnectionPlan? plan, out _))
        {
            throw new InvalidOperationException("Synthetic connection plan was rejected.");
        }

        SessionId sessionId = SessionId.New();
        NativeStartRequest request = new(sessionId, plan!, new MirroringPreferences(),
            "SYNTHETIC_USB", TransportKind.Usb, null, "synthetic-revision");
        MachineNativeHost host = new(nativeExecutable, serverPath, adbPath);
        await using INativeSession session = await host.StartAsync(request, CancellationToken.None);
        if (earlyParentDeath)
        {
            throw new InvalidOperationException("A child held before guard installation unexpectedly became ready.");
        }

        if (session is not MachineNativeSession machineSession)
        {
            throw new InvalidOperationException("Production host returned a different session type.");
        }

        string readinessPath = Path.Combine(fixtureDirectory, "supervisor-ready.json");
        string temporaryPath = readinessPath + ".tmp";
        using Process nativeProcess = Process.GetProcessById(machineSession.ProcessId);
        byte[] identity = JsonSerializer.SerializeToUtf8Bytes(new SupervisorIdentity(
            Environment.ProcessId, machineSession.ProcessId,
            nativeProcess.StartTime.ToFileTimeUtc(), sessionId.ToString()));
        await File.WriteAllBytesAsync(temporaryPath, identity);
        File.Move(temporaryPath, readinessPath);
        using EventWaitHandle readiness = EventWaitHandle.OpenExisting(readinessEventName);
        readiness.Set();

        string? stopEventName = Environment.GetEnvironmentVariable("SCRCPY_IPC_TEST_SUPERVISOR_STOP_EVENT");
        string? stoppedEventName = Environment.GetEnvironmentVariable("SCRCPY_IPC_TEST_SUPERVISOR_STOPPED_EVENT");
        if (stopEventName is not null && stoppedEventName is not null)
        {
            using EventWaitHandle stop = EventWaitHandle.OpenExisting(stopEventName);
            using EventWaitHandle stopped = EventWaitHandle.OpenExisting(stoppedEventName);
            await Task.Run(stop.WaitOne);
            if (session is not INativeInteractiveSession interactiveSession)
            {
                throw new InvalidOperationException("Machine host did not provide interactive commands.");
            }

            NativeFocusOutcome focus = await interactiveSession.FocusWindowAsync(CancellationToken.None);
            await session.StopAsync(NativeTerminationReason.UserStop, CancellationToken.None);
            NativeExit result = await session.Completion;
            bool nativeReadyObserved = false;
            bool sessionStoppedObserved = false;
            using CancellationTokenSource observationDeadline = new(TimeSpan.FromSeconds(5));
            await foreach (NativeLifecycleObservation observation in
                interactiveSession.ObserveLifecycleAsync(observationDeadline.Token))
            {
                nativeReadyObserved |= observation.EventType == NativeLifecycleEventType.NativeReady;
                sessionStoppedObserved |= observation.EventType == NativeLifecycleEventType.SessionStopped;
            }

            await session.DisposeAsync();
            string stoppedPath = Path.Combine(fixtureDirectory, "supervisor-stopped.json");
            string temporaryStoppedPath = stoppedPath + ".tmp";
            byte[] stoppedIdentity = JsonSerializer.SerializeToUtf8Bytes(new StopIdentity(
                machineSession.ProcessId, result.Reason.ToString(), focus.ToString(),
                nativeReadyObserved, sessionStoppedObserved));
            await File.WriteAllBytesAsync(temporaryStoppedPath, stoppedIdentity);
            File.Move(temporaryStoppedPath, stoppedPath);
            stopped.Set();
            return 0;
        }

        // The outer test terminates this exact supervisor process. Dispose/finally must not run.
        await Task.Delay(Timeout.InfiniteTimeSpan);
        return 0;
    }

    /// <summary>Identity published only after the production host transfers the child.</summary>
    private sealed record SupervisorIdentity(int SupervisorPid, int NativePid,
        long NativeCreatedFileTime, string SessionId);

    /// <summary>Published only after the child, pipes and session have settled.</summary>
    private sealed record StopIdentity(int NativePid, string Reason, string Focus,
        bool NativeReadyObserved, bool SessionStoppedObserved);
}
