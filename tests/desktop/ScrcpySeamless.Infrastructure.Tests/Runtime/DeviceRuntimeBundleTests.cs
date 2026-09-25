using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ScrcpySeamless.Infrastructure.Runtime;
using Xunit;

namespace ScrcpySeamless.Infrastructure.Tests.Runtime;

/// <summary>Validates a synthetic DEV bundle without executing any component.</summary>
public sealed class DeviceRuntimeBundleTests
{
    private static readonly string[] FileNames =
    [
        "scrcpy.exe", "scrcpy-server", "adb.exe", "AdbWinApi.dll", "AdbWinUsbApi.dll",
        "SDL3.dll", "avcodec-62.dll", "avformat-62.dll", "avutil-60.dll",
        "swresample-6.dll", "scrcpy.png", "disconnected.png",
    ];

    /// <summary>An exact manifest resolves explicit native, server and ADB paths containing spaces.</summary>
    [Fact]
    public void ValidatesExplicitBundleWithSpaces()
    {
        using SyntheticBundle fixture = new();

        RuntimeBundleResult result = DeviceRuntimeBundle.Validate(fixture.Directory);

        Assert.Equal(RuntimeBundleStatus.Ready, result.Status);
        Assert.Equal(Path.Combine(fixture.Directory, "scrcpy.exe"), result.Bundle!.NativeExecutablePath);
        Assert.Equal(Path.Combine(fixture.Directory, "scrcpy-server"), result.Bundle.ServerPath);
        Assert.Equal(Path.Combine(fixture.Directory, "adb.exe"), result.Bundle.AdbExecutablePath);
    }

    /// <summary>Changed or missing runtime bytes block launch before any process creation.</summary>
    [Fact]
    public void RejectsMissingAndTamperedComponents()
    {
        using SyntheticBundle fixture = new();
        string serverPath = Path.Combine(fixture.Directory, "scrcpy-server");
        File.WriteAllText(serverPath, "tampered");
        RuntimeBundleResult tampered = DeviceRuntimeBundle.Validate(fixture.Directory);
        Assert.Equal(RuntimeBundleStatus.HashMismatch, tampered.Status);
        Assert.Equal("scrcpy-server", tampered.Component);

        File.Delete(serverPath);
        RuntimeBundleResult missing = DeviceRuntimeBundle.Validate(fixture.Directory);
        Assert.Equal(RuntimeBundleStatus.MissingComponent, missing.Status);
        Assert.Equal("scrcpy-server", missing.Component);
    }

    /// <summary>An absent or nonabsolute bundle never falls back to PATH or the working directory.</summary>
    [Fact]
    public void RejectsAbsentAndRelativeBundle()
    {
        using SyntheticBundle fixture = new();
        File.Delete(Path.Combine(fixture.Directory, DeviceRuntimeBundle.ManifestFileName));

        Assert.Equal(RuntimeBundleStatus.Missing, DeviceRuntimeBundle.Validate(fixture.Directory).Status);
        Assert.Equal(RuntimeBundleStatus.InvalidManifest, DeviceRuntimeBundle.Validate("relative-runtime").Status);
    }

    /// <summary>Owns only disposable fake component files and a matching hash manifest.</summary>
    private sealed class SyntheticBundle : IDisposable
    {
        public SyntheticBundle()
        {
            Directory = Path.Combine(Path.GetTempPath(), "scrcpy p05c synthetic " + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
            Dictionary<string, string> hashes = [];
            Dictionary<string, string> origins = [];

            foreach (string name in FileNames)
            {
                byte[] bytes = Encoding.UTF8.GetBytes("synthetic:" + name);
                File.WriteAllBytes(Path.Combine(Directory, name), bytes);
                hashes[name] = Convert.ToHexString(SHA256.HashData(bytes));
                origins[name] = name is "scrcpy.exe" or "scrcpy-server" ? "source-built" : "reviewed-import";
            }

            DeviceRuntimeManifest manifest = new()
            {
                SchemaVersion = 1,
                SourceSha = new string('a', 40),
                NativeSourceFingerprint = new string('b', 64),
                ServerSourceFingerprint = new string('c', 64),
                Files = hashes,
                Origins = origins,
            };
            File.WriteAllText(Path.Combine(Directory, DeviceRuntimeBundle.ManifestFileName),
                JsonSerializer.Serialize(manifest));
        }

        public string Directory { get; }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
