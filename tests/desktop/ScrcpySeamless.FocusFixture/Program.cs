using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Application.Connection;
using ScrcpySeamless.Core.Application.NativeHost;
using ScrcpySeamless.Core.Configuration;
using ScrcpySeamless.Infrastructure.NativeHost;

namespace ScrcpySeamless.FocusFixture;

/// <summary>Runs an opt-in, human-clicked no-phone foreground experiment over the production host.</summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] arguments)
    {
        if (arguments is not [string nativeExecutable, string evidenceDirectory])
        {
            throw new ArgumentException("Expected isolated test_machine_child.exe and a new evidence directory.");
        }

        ApplicationConfiguration.Initialize();
        System.Windows.Forms.Application.Run(new FocusForm(nativeExecutable, evidenceDirectory));
    }
}

/// <summary>Measures foreground without activating any window during either one-shot request.</summary>
internal sealed class FocusForm : Form
{
    private static readonly TimeSpan ObservationLimit = TimeSpan.FromSeconds(2);
    private const int ObservationIntervalMilliseconds = 20;
    private readonly string nativeExecutable;
    private readonly string evidenceDirectory;
    private readonly Button baseline = new() { Text = "1. Focus with current production route", AutoSize = true, Enabled = false };
    private readonly Button delegated = new() { Text = "2. Focus with extra exact-child permission grant", AutoSize = true, Enabled = false };
    private readonly Label status = new() { Text = "Waiting for isolated native readiness", AutoSize = true };
    private MachineNativeSession? session;
    private Process? child;
    private nint childWindow;
    private bool closing;
    private bool commandRunning;
    private bool starting = true;

    /// <summary>Builds only synthetic controls; launch is deferred to the shown event.</summary>
    public FocusForm(string nativeExecutable, string evidenceDirectory)
    {
        this.nativeExecutable = Path.GetFullPath(nativeExecutable);
        this.evidenceDirectory = Path.GetFullPath(evidenceDirectory);

        if (Path.GetFileName(this.nativeExecutable) != "test_machine_child.exe" || Directory.Exists(this.evidenceDirectory))
        {
            throw new ArgumentException("Require the isolated fixture and an unused evidence directory.");
        }

        Text = "Seamless synthetic Focus parent";
        Width = 610;
        Height = 220;
        FlowLayoutPanel panel = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(16) };
        panel.Controls.AddRange([baseline, delegated, status]);
        Controls.Add(panel);
        Shown += async (_, _) => await StartChildAsync();
        baseline.Click += async (_, _) => await ObserveFocusAsync(false);
        delegated.Click += async (_, _) => await ObserveFocusAsync(true);
        FormClosing += OnClosing;
    }

    /// <summary>Starts only the production-linked no-device child, with synthetic unused component paths.</summary>
    private async Task StartChildAsync()
    {
        Directory.CreateDirectory(evidenceDirectory);
        string server = Path.Combine(evidenceDirectory, "synthetic-server");
        string adb = Path.Combine(evidenceDirectory, "synthetic-adb.exe");
        File.WriteAllBytes(server, []);
        File.WriteAllBytes(adb, []);
        Environment.SetEnvironmentVariable("SCRCPY_IPC_TEST_DIRECTORY", evidenceDirectory);
        Environment.SetEnvironmentVariable("SCRCPY_IPC_TEST_FOCUS_WINDOW", "1");
        DeviceProfile profile = new() { Id = ProfileId.New(), UsbIdentity = new UsbSerial("SYNTHETIC_USB") };

        if (!ConnectionPlan.TryCreate(profile, ConnectionPolicy.Default, out ConnectionPlan? plan, out _))
        {
            throw new InvalidOperationException("Synthetic plan rejected.");
        }

        NativeStartRequest request = new(SessionId.New(), plan!, new MirroringPreferences(),
            "SYNTHETIC_USB", TransportKind.Usb, null, "synthetic-revision");
        MachineNativeHost host = new(nativeExecutable, server, adb);
        session = (MachineNativeSession)await host.StartAsync(request, CancellationToken.None);
        child = Process.GetProcessById(session.ProcessId);
        _ = child.Handle;
        using JsonDocument marker = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(evidenceDirectory, "focus-window-ready.json")));
        childWindow = (nint)marker.RootElement.GetProperty("Hwnd").GetInt64();
        uint windowThread = NativeMethods.GetWindowThreadProcessId(childWindow, out uint windowOwner);

        if (marker.RootElement.GetProperty("Pid").GetInt32() != child.Id ||
            windowOwner != (uint)child.Id || windowThread != marker.RootElement.GetProperty("MainThreadId").GetUInt32() ||
            !string.Equals(child.MainModule!.FileName, nativeExecutable, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Synthetic window did not belong to the owned fixture child.");
        }
        ProcessModule sdl = child.Modules.Cast<ProcessModule>().Single(module => module.ModuleName.Equals("SDL3.dll", StringComparison.OrdinalIgnoreCase));
        WriteEvidence("parent-ready.json", new
        {
            ParentPid = Environment.ProcessId,
            ParentCreatedUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime().ToString("O"),
            NativePid = child.Id,
            NativeCreatedUtc = child.StartTime.ToUniversalTime().ToString("O"),
            NativeExecutable = child.MainModule!.FileName,
            NativeHwnd = childWindow.ToInt64(),
            LoadedSdlPath = sdl.FileName,
            LoadedSdlSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(sdl.FileName))),
            SessionId = session.SessionId.ToString(),
            Handshake = session.AcceptedHandshake,
        });
        baseline.Enabled = true;
        starting = false;
        status.Text = "Click 1 once, then return to this parent and click 2 once. No tool interaction during measurement.";
    }

    /// <summary>Separates the correlated native result, optional grant result and observed foreground owner.</summary>
    private async Task ObserveFocusAsync(bool grantPermission)
    {
        if (commandRunning || session is null || child is null || child.HasExited)
        {
            return;
        }

        commandRunning = true;
        baseline.Enabled = false;
        delegated.Enabled = false;
        string before = ForegroundOwner();

        if (before != "parent")
        {
            WriteEvidence("invalid-input-precondition.json", new { ForegroundBefore = before, Route = grantPermission ? "delegated" : "baseline" });
            status.Text = "Input precondition failed; stop and review, do not repeat.";
            commandRunning = false;
            return;
        }

        bool? granted = null;
        int? grantError = null;

        if (grantPermission)
        {
            granted = NativeMethods.AllowSetForegroundWindow((uint)child.Id);

            if (!granted.Value)
            {
                grantError = Marshal.GetLastPInvokeError();
            }
        }

        NativeFocusOutcome outcome = await session.FocusWindowAsync(CancellationToken.None);
        Stopwatch observation = Stopwatch.StartNew();
        string after = ForegroundOwner();

        while (after != "child" && observation.Elapsed < ObservationLimit)
        {
            await Task.Delay(ObservationIntervalMilliseconds);
            after = ForegroundOwner();
        }

        WriteEvidence(grantPermission ? "02-delegated.json" : "01-baseline.json", new
        {
            CapturedUtc = DateTimeOffset.UtcNow,
            Route = grantPermission ? "ExactChildGrantThenProductionFocus" : "ProductionFocus",
            ForegroundBefore = before,
            ForegroundAfter = after,
            ExtraGrantResult = granted,
            ExtraGrantError = grantError,
            ProductionGrantResult = "Unobserved",
            CommandResult = outcome.ToString(),
            NativePlatformReturn = "NotExposedBySDL",
            NativePid = session.ProcessId,
            NativeCreatedUtc = child.StartTime.ToUniversalTime().ToString("O"),
            NativeHwnd = childWindow.ToInt64(),
            Visible = NativeMethods.IsWindowVisible(childWindow),
            Minimized = NativeMethods.IsIconic(childWindow),
            SessionCompleted = session.Completion.IsCompleted,
            ObservationMilliseconds = observation.ElapsedMilliseconds,
        });
        status.Text = $"{outcome}; foreground: {before} → {after}; extra grant: {granted?.ToString() ?? "not requested"}; production grant: unobserved";
        delegated.Enabled = !grantPermission;
        commandRunning = false;
    }

    /// <summary>Classifies only owned windows; never reads unrelated titles or changes focus.</summary>
    private string ForegroundOwner()
    {
        nint window = NativeMethods.GetForegroundWindow();

        if (window == Handle)
        {
            return "parent";
        }

        return window == childWindow ? "child" : "other";
    }

    /// <summary>Publishes a complete new local observation without replacing an earlier checkpoint.</summary>
    private void WriteEvidence(string name, object evidence)
    {
        using FileStream stream = new(Path.Combine(evidenceDirectory, name), FileMode.CreateNew);
        JsonSerializer.Serialize(stream, evidence, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Settles only the owned fixture through ordinary production Stop/disposal before closing.</summary>
    private async void OnClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (closing)
        {
            return;
        }

        eventArgs.Cancel = true;

        if (commandRunning || starting)
        {
            status.Text = "Wait for the current bounded measurement before closing.";
            return;
        }

        if (session is not null)
        {
            await session.DisposeAsync();
        }

        child?.Dispose();
        closing = true;
        Close();
    }
}

/// <summary>Contains only supported exact-child delegation and read-only foreground/window measurement.</summary>
internal static partial class NativeMethods
{
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AllowSetForegroundWindow(uint processId);

    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(nint window);

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(nint window, out uint processId);
}
