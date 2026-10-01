using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScrcpySeamless.Infrastructure.NativeProtocol;

namespace ScrcpySeamless.Infrastructure.Runtime;

/// <summary>Result of validating an explicit, non-release development runtime.</summary>
public enum RuntimeBundleStatus
{
    Ready,
    Missing,
    InvalidManifest,
    MissingComponent,
    HashMismatch,
    IncompatibleMachineContract,
}

/// <summary>Resolved files or a structured reason to keep device launch unavailable.</summary>
public sealed record RuntimeBundleResult(DeviceRuntimeBundle? Bundle, RuntimeBundleStatus Status, string? Component);

/// <summary>Hashed DEV runtime inputs; origin labels describe provenance, not a release attestation.</summary>
public sealed class DeviceRuntimeManifest
{
    public int SchemaVersion { get; init; }
    public string SourceSha { get; init; } = string.Empty;
    public string NativeSourceFingerprint { get; init; } = string.Empty;
    public string ServerSourceFingerprint { get; init; } = string.Empty;
    public Dictionary<string, string> Files { get; init; } = [];
    public Dictionary<string, string> Origins { get; init; } = [];
    public MachineRuntimeContract? MachineContract { get; init; }
}

/// <summary>Describes the native machine contract claimed by a hashed DEV bundle.</summary>
public sealed class MachineRuntimeContract
{
    public string Product { get; init; } = string.Empty;
    public int ProtocolMajor { get; init; }
    public int ProtocolMinor { get; init; }
    public string[]? Capabilities { get; init; }
}

/// <summary>Validates the exact files selected for a device-enabled Desktop session.</summary>
public sealed class DeviceRuntimeBundle
{
    public const string ManifestFileName = "runtime-dev-manifest.json";
    private static readonly string[] RequiredFiles =
    [
        "scrcpy.exe", "scrcpy-server", "adb.exe", "AdbWinApi.dll", "AdbWinUsbApi.dll",
        "SDL3.dll", "avcodec-62.dll", "avformat-62.dll", "avutil-60.dll",
        "swresample-6.dll", "scrcpy.png", "disconnected.png",
    ];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private DeviceRuntimeBundle(string directory, DeviceRuntimeManifest manifest)
    {
        Directory = directory;
        Manifest = manifest;
    }

    public string Directory { get; }
    public DeviceRuntimeManifest Manifest { get; }
    public string NativeExecutablePath => Path.Combine(Directory, "scrcpy.exe");
    public string ServerPath => Path.Combine(Directory, "scrcpy-server");
    public string AdbExecutablePath => Path.Combine(Directory, "adb.exe");

    /// <summary>Reads only a caller-selected directory and checks every required component before ADB or native launch.</summary>
    public static RuntimeBundleResult Validate(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
        {
            return new RuntimeBundleResult(null, RuntimeBundleStatus.InvalidManifest, null);
        }

        string fullDirectory;

        try
        {
            fullDirectory = Path.GetFullPath(directory);
        }
        catch (ArgumentException)
        {
            return new RuntimeBundleResult(null, RuntimeBundleStatus.InvalidManifest, null);
        }
        string manifestPath = Path.Combine(fullDirectory, ManifestFileName);

        if (!File.Exists(manifestPath))
        {
            return new RuntimeBundleResult(null, RuntimeBundleStatus.Missing, ManifestFileName);
        }

        DeviceRuntimeManifest? manifest;
        bool machineContractDeclared = false;

        try
        {
            string manifestJson = File.ReadAllText(manifestPath);
            using JsonDocument document = JsonDocument.Parse(manifestJson);
            machineContractDeclared = document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty(nameof(DeviceRuntimeManifest.MachineContract), out _);
            manifest = JsonSerializer.Deserialize<DeviceRuntimeManifest>(manifestJson, JsonOptions);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new RuntimeBundleResult(null, RuntimeBundleStatus.InvalidManifest, ManifestFileName);
        }

        bool invalidManifest = manifest is null || manifest.SchemaVersion != 1 ||
            manifest.SourceSha is not { Length: 40 } || !manifest.SourceSha.All(Uri.IsHexDigit) ||
            manifest.NativeSourceFingerprint is not { Length: 64 } ||
            !manifest.NativeSourceFingerprint.All(Uri.IsHexDigit) ||
            manifest.ServerSourceFingerprint is not { Length: 64 } ||
            !manifest.ServerSourceFingerprint.All(Uri.IsHexDigit) ||
            manifest.Files is null || manifest.Origins is null ||
            manifest.Files.Count != RequiredFiles.Length || manifest.Origins.Count != RequiredFiles.Length ||
            RequiredFiles.Any(name => !manifest.Files.TryGetValue(name, out string? hash) ||
                hash is null || hash.Length != 64 || !hash.All(Uri.IsHexDigit) ||
                !manifest.Origins.TryGetValue(name, out string? origin) ||
                origin != (name is "scrcpy.exe" or "scrcpy-server" ? "source-built" : "reviewed-import"));

        if (invalidManifest)
        {
            return new RuntimeBundleResult(null, RuntimeBundleStatus.InvalidManifest, ManifestFileName);
        }

        foreach (string name in RequiredFiles)
        {
            string path = Path.Combine(fullDirectory, name);

            if (!File.Exists(path))
            {
                return new RuntimeBundleResult(null, RuntimeBundleStatus.MissingComponent, name);
            }

            try
            {
                string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

                if (!string.Equals(actual, manifest!.Files![name], StringComparison.OrdinalIgnoreCase))
                {
                    return new RuntimeBundleResult(null, RuntimeBundleStatus.HashMismatch, name);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new RuntimeBundleResult(null, RuntimeBundleStatus.MissingComponent, name);
            }
        }

        // A schema-v1 manifest without this later claim remains a recognizable legacy-only bundle.
        MachineRuntimeContract? contract = manifest!.MachineContract;

        if (contract is null)
        {
            RuntimeBundleStatus status = machineContractDeclared
                ? RuntimeBundleStatus.InvalidManifest
                : RuntimeBundleStatus.IncompatibleMachineContract;
            return new RuntimeBundleResult(null, status, ManifestFileName);
        }

        bool malformedContract = string.IsNullOrWhiteSpace(contract.Product) ||
            Encoding.UTF8.GetByteCount(contract.Product) > 256 || contract.Product.Contains('\0') ||
            contract.ProtocolMajor is < 0 or > ushort.MaxValue ||
            contract.ProtocolMinor is < 0 or > ushort.MaxValue ||
            contract.Capabilities is null || contract.Capabilities.Length is < 1 or > 16 ||
            contract.Capabilities.Any(capability => string.IsNullOrEmpty(capability) ||
                Encoding.UTF8.GetByteCount(capability) > 64 || capability.Contains('\0')) ||
            contract.Capabilities.Distinct(StringComparer.Ordinal).Count() != contract.Capabilities.Length;

        if (malformedContract)
        {
            return new RuntimeBundleResult(null, RuntimeBundleStatus.InvalidManifest, ManifestFileName);
        }

        bool compatibleContract = contract.Product == ProtocolCompatibility.Product &&
            contract.ProtocolMajor == ProtocolCompatibility.Major &&
            contract.ProtocolMinor >= ProtocolCompatibility.Minor &&
            ProtocolCompatibility.RequiredCapabilities.All(capability =>
                contract.Capabilities!.Contains(capability, StringComparer.Ordinal));

        if (!compatibleContract)
        {
            return new RuntimeBundleResult(null, RuntimeBundleStatus.IncompatibleMachineContract, ManifestFileName);
        }

        return new RuntimeBundleResult(new DeviceRuntimeBundle(fullDirectory, manifest), RuntimeBundleStatus.Ready, null);
    }
}
