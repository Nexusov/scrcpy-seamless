using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ScrcpySeamless.Infrastructure.NativeProtocol;
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

    /// <summary>The staging claim follows the exact managed handshake version and required capabilities.</summary>
    [Fact]
    public void StagingContractMatchesManagedProtocol()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "NativeProtocolContract", "runtime-contract.json");
        MachineRuntimeContract? claim = JsonSerializer.Deserialize<MachineRuntimeContract>(File.ReadAllText(path));

        Assert.NotNull(claim);
        Assert.Equal(ProtocolCompatibility.Product, claim.Product);
        Assert.Equal(ProtocolCompatibility.Major, claim.ProtocolMajor);
        Assert.Equal(ProtocolCompatibility.Minor, claim.ProtocolMinor);
        Assert.Equal(ProtocolCompatibility.RequiredCapabilities, claim.Capabilities);
    }

    /// <summary>A hash-valid old bundle remains recognizable but cannot launch the machine route.</summary>
    [Fact]
    public void LegacyManifestHasExplicitMachineIncompatibility()
    {
        using SyntheticBundle fixture = new();
        string manifestPath = Path.Combine(fixture.Directory, DeviceRuntimeBundle.ManifestFileName);
        JsonObject manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        manifest.Remove("MachineContract");
        byte[] oldBytes = Encoding.UTF8.GetBytes(manifest.ToJsonString());
        File.WriteAllBytes(manifestPath, oldBytes);

        RuntimeBundleResult result = DeviceRuntimeBundle.Validate(fixture.Directory);

        Assert.Equal(RuntimeBundleStatus.IncompatibleMachineContract, result.Status);
        Assert.Null(result.Bundle);
        Assert.Equal(DeviceRuntimeBundle.ManifestFileName, result.Component);
        Assert.Equal(oldBytes, File.ReadAllBytes(manifestPath));
    }

    /// <summary>An explicit null claim is malformed; only absent metadata identifies an older manifest.</summary>
    [Fact]
    public void RejectsExplicitNullMachineClaim()
    {
        using SyntheticBundle fixture = new();
        string manifestPath = Path.Combine(fixture.Directory, DeviceRuntimeBundle.ManifestFileName);
        JsonObject manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        manifest["MachineContract"] = null;
        File.WriteAllText(manifestPath, manifest.ToJsonString());

        Assert.Equal(RuntimeBundleStatus.InvalidManifest, DeviceRuntimeBundle.Validate(fixture.Directory).Status);
    }

    /// <summary>A coherent but wrong machine claim cannot turn a legacy or incompatible peer into a launch.</summary>
    [Theory]
    [InlineData("Product", "\"another-product\"")]
    [InlineData("ProtocolMajor", "2")]
    [InlineData("Capabilities", "[\"focus-window\",\"stop\"]")]
    public void RejectsIncompatibleMachineClaim(string member, string replacementJson)
    {
        using SyntheticBundle fixture = new();
        string manifestPath = Path.Combine(fixture.Directory, DeviceRuntimeBundle.ManifestFileName);
        JsonObject manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        manifest["MachineContract"]!.AsObject()[member] = JsonNode.Parse(replacementJson);
        File.WriteAllText(manifestPath, manifest.ToJsonString());

        RuntimeBundleResult result = DeviceRuntimeBundle.Validate(fixture.Directory);

        Assert.Equal(RuntimeBundleStatus.IncompatibleMachineContract, result.Status);
        Assert.Null(result.Bundle);
    }

    /// <summary>Incomplete or ill-typed claims are malformed metadata, not a recognized old bundle.</summary>
    [Theory]
    [InlineData("Capabilities", "null")]
    [InlineData("Capabilities", "[\"stop\",\"stop\"]")]
    [InlineData("Capabilities", "42")]
    [InlineData("ProtocolMajor", "-1")]
    [InlineData("Product", "null")]
    public void RejectsMalformedMachineClaim(string member, string replacementJson)
    {
        using SyntheticBundle fixture = new();
        string manifestPath = Path.Combine(fixture.Directory, DeviceRuntimeBundle.ManifestFileName);
        JsonObject manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        manifest["MachineContract"]!.AsObject()[member] = JsonNode.Parse(replacementJson);
        File.WriteAllText(manifestPath, manifest.ToJsonString());

        RuntimeBundleResult result = DeviceRuntimeBundle.Validate(fixture.Directory);

        Assert.Equal(RuntimeBundleStatus.InvalidManifest, result.Status);
        Assert.Null(result.Bundle);
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

    /// <summary>Malformed required manifest values leave the original bytes intact and report an invalid runtime.</summary>
    [Theory]
    [InlineData("SourceSha", "null")]
    [InlineData("NativeSourceFingerprint", "null")]
    [InlineData("ServerSourceFingerprint", "null")]
    [InlineData("SourceSha", null)]
    [InlineData("NativeSourceFingerprint", null)]
    [InlineData("ServerSourceFingerprint", null)]
    [InlineData("SourceSha", "42")]
    [InlineData("NativeSourceFingerprint", "{}")]
    [InlineData("ServerSourceFingerprint", "[]")]
    [InlineData("SchemaVersion", "2")]
    [InlineData("SchemaVersion", null)]
    [InlineData("Files", "null")]
    [InlineData("Origins", "null")]
    [InlineData("Files", null)]
    [InlineData("Origins", null)]
    [InlineData("Files", "42")]
    [InlineData("Origins", "[]")]
    [InlineData("Files.scrcpy.exe", "null")]
    [InlineData("Origins.scrcpy.exe", "null")]
    [InlineData("Files.scrcpy.exe", "42")]
    [InlineData("Origins.scrcpy.exe", "42")]
    public void RejectsMalformedManifestWithoutChangingIt(string member, string? replacementJson)
    {
        using SyntheticBundle fixture = new();
        string manifestPath = Path.Combine(fixture.Directory, DeviceRuntimeBundle.ManifestFileName);
        JsonObject manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        string[] path = member.Split('.', count: 2);
        JsonObject owner = path.Length == 1 ? manifest : manifest[path[0]]!.AsObject();
        string name = path[^1];

        if (replacementJson is null)
        {
            owner.Remove(name);
        }
        else
        {
            owner[name] = JsonNode.Parse(replacementJson);
        }

        byte[] malformedBytes = Encoding.UTF8.GetBytes(manifest.ToJsonString());
        File.WriteAllBytes(manifestPath, malformedBytes);

        RuntimeBundleResult result = DeviceRuntimeBundle.Validate(fixture.Directory);

        Assert.Equal(RuntimeBundleStatus.InvalidManifest, result.Status);
        Assert.Null(result.Bundle);
        Assert.Equal(DeviceRuntimeBundle.ManifestFileName, result.Component);
        Assert.Equal(malformedBytes, File.ReadAllBytes(manifestPath));
    }

    /// <summary>Only the tested ADB 34.0.5 bytes receive the Openscreen compatibility setting.</summary>
    [Theory]
    [InlineData("58765259A349CCE392FBB2F15DAB75FED3B7C0B40CC68A7653278B9850602A2F", true)]
    [InlineData("58765259a349cce392fbb2f15dab75fed3b7c0b40cc68a7653278b9850602a2f", true)]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000", false)]
    public void MdnsCompatibilityMatchesExactReviewedAdb(string adbSha256, bool expected)
    {
        Assert.Equal(expected, BundledAdbCompatibility.RequiresOpenScreenMdns(adbSha256));
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
                MachineContract = new MachineRuntimeContract
                {
                    Product = ProtocolCompatibility.Product,
                    ProtocolMajor = ProtocolCompatibility.Major,
                    ProtocolMinor = ProtocolCompatibility.Minor,
                    Capabilities = ProtocolCompatibility.RequiredCapabilities.ToArray(),
                },
            };
            File.WriteAllText(Path.Combine(Directory, DeviceRuntimeBundle.ManifestFileName),
                JsonSerializer.Serialize(manifest));
        }

        public string Directory { get; }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
