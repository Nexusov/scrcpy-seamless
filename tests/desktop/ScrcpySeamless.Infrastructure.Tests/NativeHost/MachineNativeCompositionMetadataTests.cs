using System.Diagnostics;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Adb;
using ScrcpySeamless.Core.Application.Connection;
using ScrcpySeamless.Core.Application.NativeHost;
using ScrcpySeamless.Core.Configuration;
using ScrcpySeamless.Infrastructure.NativeHost;
using Xunit;

namespace ScrcpySeamless.Infrastructure.Tests.NativeHost;

/// <summary>Verifies the real machine host's launch identity without starting ADB or native code.</summary>
public sealed class MachineNativeCompositionMetadataTests
{
    /// <summary>A Desktop-composed machine child receives its actual owning process identity.</summary>
    [Fact]
    public void LaunchMetadataUsesCurrentOwnerAndNoLegacyStopChannel()
    {
        string directory = Path.Combine(Path.GetTempPath(), "scrcpy machine metadata " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            string nativePath = WriteInertFile(directory, "scrcpy.exe");
            string serverPath = WriteInertFile(directory, "scrcpy-server");
            string adbPath = WriteInertFile(directory, "adb.exe");
            MachineNativeHost host = new(nativePath, serverPath, adbPath);
            DeviceProfile profile = new()
            {
                Id = ProfileId.New(),
                UsbIdentity = new UsbSerial("SYNTHETIC_USB"),
            };
            Assert.True(ConnectionPlan.TryCreate(profile, ConnectionPolicy.Default,
                out ConnectionPlan? plan, out _));
            NativeStartRequest request = new(SessionId.New(), plan!, new MirroringPreferences(),
                "SYNTHETIC_USB", TransportKind.Usb, null, "synthetic-revision");

            ProcessStartInfo startInfo = host.CreateStartInfo(request);
            using Process owner = Process.GetCurrentProcess();
            long ownerCreated = owner.StartTime.ToUniversalTime().ToFileTimeUtc();

            Assert.Equal(nativePath, startInfo.FileName);
            Assert.Equal($"--seamless-parent-pid={owner.Id}",
                Assert.Single(startInfo.ArgumentList, argument =>
                    argument.StartsWith("--seamless-parent-pid=", StringComparison.Ordinal)));
            Assert.Equal($"--seamless-parent-created={ownerCreated}",
                Assert.Single(startInfo.ArgumentList, argument =>
                    argument.StartsWith("--seamless-parent-created=", StringComparison.Ordinal)));
            Assert.Contains($"--seamless-session-id={request.SessionId.Value:D}", startInfo.ArgumentList);
            Assert.Contains("--seamless-machine", startInfo.ArgumentList);
            Assert.True(startInfo.RedirectStandardInput && startInfo.RedirectStandardOutput &&
                startInfo.RedirectStandardError);
            Assert.False(startInfo.Environment.ContainsKey("SCRCPY_STOP_EVENT"));
            Assert.False(startInfo.Environment.ContainsKey("SCRCPY_RECONNECT_SERIAL"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Creates one non-executable placeholder for path-only launch metadata.</summary>
    private static string WriteInertFile(string directory, string fileName)
    {
        string path = Path.Combine(directory, fileName);
        File.WriteAllText(path, "inert");
        return path;
    }
}
