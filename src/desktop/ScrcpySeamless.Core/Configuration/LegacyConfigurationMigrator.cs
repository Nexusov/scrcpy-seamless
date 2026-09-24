using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScrcpySeamless.Core.Configuration;

/// <summary>Machine-readable reason a legacy document cannot be migrated safely.</summary>
public enum LegacyMigrationProblemCode
{
    InvalidJson,
    InvalidShape,
    MissingRequiredField,
    UnsupportedField,
    UnsupportedValue,
    InconsistentTransport,
}

/// <summary>Identifies the source and field without copying user data into diagnostics.</summary>
public sealed record LegacyMigrationProblem(LegacyMigrationProblemCode Code, string Source, string Field);

/// <summary>A pure migration result; callers commit only when no problems are present.</summary>
public sealed record LegacyMigrationResult(
    ConfigurationV2? Configuration,
    IReadOnlyList<LegacyMigrationProblem> Problems,
    bool HadLegacyInputs)
{
    public bool Succeeded => Configuration is not null && Problems.Count == 0;
}

/// <summary>Converts the two independent Seamless 1.x documents without filesystem effects.</summary>
public static class LegacyConfigurationMigrator
{
    private const string PhoneSource = "phone.json";
    private const string SettingsSource = "scrcpy-settings.json";
    private const string MdnsConnectSuffix = "._adb-tls-connect._tcp";

    /// <summary>Validates all legacy fields before constructing the versioned configuration.</summary>
    public static LegacyMigrationResult Migrate(string? phoneJson, string? settingsJson)
    {
        List<LegacyMigrationProblem> problems = [];
        DeviceProfile? profile = ParsePhone(phoneJson, problems);
        MirroringPreferences? mirroring = ParseSettings(settingsJson, problems);

        if (problems.Count > 0 || mirroring is null)
        {
            return new LegacyMigrationResult(null, problems, phoneJson is not null || settingsJson is not null);
        }

        ConfigurationV2 configuration = new()
        {
            Profiles = profile is null ? [] : [profile],
            Mirroring = mirroring,
        };

        foreach (ValidationIssue issue in configuration.Validate())
        {
            problems.Add(new LegacyMigrationProblem(LegacyMigrationProblemCode.UnsupportedValue, "configuration.v2.json", issue.Path));
        }

        return problems.Count == 0
            ? new LegacyMigrationResult(configuration, problems, phoneJson is not null || settingsJson is not null)
            : new LegacyMigrationResult(null, problems, phoneJson is not null || settingsJson is not null);
    }

    /// <summary>Maps a legacy singleton phone into one profile with a deterministic initial identity.</summary>
    private static DeviceProfile? ParsePhone(string? json, List<LegacyMigrationProblem> problems)
    {
        if (json is null)
        {
            return null;
        }

        using JsonDocument? document = ParseDocument(json, PhoneSource, problems);

        if (document is null)
        {
            return null;
        }

        JsonElement root = document.RootElement;
        ValidateProperties(root, PhoneSource, ["UsbSerial", "WirelessService", "ConnectionMode"], problems);
        string? serialText = ReadString(root, "UsbSerial", PhoneSource, required: true, problems);
        string? wirelessService = ReadString(root, "WirelessService", PhoneSource, required: false, problems) ?? string.Empty;
        string? modeText = ReadString(root, "ConnectionMode", PhoneSource, required: false, problems);

        if (serialText is null || problems.Count > 0)
        {
            return null;
        }

        if (serialText == "YOUR_USB_SERIAL" ||
            !Regex.IsMatch(serialText, "^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant))
        {
            problems.Add(new LegacyMigrationProblem(LegacyMigrationProblemCode.UnsupportedValue, PhoneSource, "UsbSerial"));
            return null;
        }

        UsbSerial serial;

        try
        {
            serial = new UsbSerial(serialText);
        }
        catch (ArgumentException)
        {
            problems.Add(new LegacyMigrationProblem(LegacyMigrationProblemCode.UnsupportedValue, PhoneSource, "UsbSerial"));
            return null;
        }

        modeText ??= wirelessService.Length > 0 ? "auto" : "usb";
        TransportPreference preference = modeText switch
        {
            "usb" => TransportPreference.Usb,
            "wifi" => TransportPreference.Network,
            "auto" => TransportPreference.Automatic,
            _ => (TransportPreference)(-1),
        };

        if (!Enum.IsDefined(preference))
        {
            problems.Add(new LegacyMigrationProblem(LegacyMigrationProblemCode.UnsupportedValue, PhoneSource, "ConnectionMode"));
            return null;
        }

        bool modeNeedsNetwork = preference != TransportPreference.Usb;

        if (modeNeedsNetwork == (wirelessService.Length == 0))
        {
            problems.Add(new LegacyMigrationProblem(LegacyMigrationProblemCode.InconsistentTransport, PhoneSource, "WirelessService"));
            return null;
        }

        NetworkEndpoint? connectionEndpoint = null;
        MdnsServiceName? mdnsIdentity = null;

        string expectedMdnsPrefix = "adb-" + serial.Value + "-";
        bool isLegacyMdns = wirelessService.StartsWith(expectedMdnsPrefix, StringComparison.Ordinal)
            && wirelessService.EndsWith(MdnsConnectSuffix, StringComparison.Ordinal)
            && wirelessService.Length > expectedMdnsPrefix.Length + MdnsConnectSuffix.Length
            && wirelessService[expectedMdnsPrefix.Length..^MdnsConnectSuffix.Length]
                .All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

        if (wirelessService.Length > 0 && isLegacyMdns)
        {
            try
            {
                mdnsIdentity = new MdnsServiceName(wirelessService);
            }
            catch (ArgumentException)
            {
                problems.Add(new LegacyMigrationProblem(LegacyMigrationProblemCode.UnsupportedValue, PhoneSource, "WirelessService"));
                return null;
            }
        }
        else if (wirelessService.Length > 0 &&
                 (!NetworkEndpoint.TryParse(wirelessService, out connectionEndpoint) ||
                  connectionEndpoint?.HostKind != EndpointHostKind.Ipv4))
        {
            problems.Add(new LegacyMigrationProblem(LegacyMigrationProblemCode.UnsupportedValue, PhoneSource, "WirelessService"));
            return null;
        }

        byte[] identityBytes = SHA256.HashData(Encoding.UTF8.GetBytes("scrcpy-seamless/legacy-profile-v1\0" + serial.Value));
        ProfileId profileId = new(new Guid(identityBytes[..16]));

        return new DeviceProfile
        {
            Id = profileId,
            UsbIdentity = serial,
            MdnsIdentity = mdnsIdentity,
            ConnectionEndpoint = connectionEndpoint,
            Connection = new ConnectionPreferences
            {
                PreferredTransport = preference,
                AllowFallback = preference == TransportPreference.Automatic,
            },
        };
    }

    /// <summary>Preserves legacy mirroring overrides without interpreting Phase 4 option semantics.</summary>
    private static MirroringPreferences? ParseSettings(string? json, List<LegacyMigrationProblem> problems)
    {
        if (json is null)
        {
            return new MirroringPreferences();
        }

        using JsonDocument? document = ParseDocument(json, SettingsSource, problems);

        if (document is null)
        {
            return null;
        }

        JsonElement root = document.RootElement;
        ValidateProperties(root, SettingsSource, ["SchemaVersion", "Reconnect", "Options"], problems);

        if (!root.TryGetProperty("SchemaVersion", out JsonElement schemaVersion) ||
            schemaVersion.ValueKind != JsonValueKind.Number ||
            !schemaVersion.TryGetInt32(out int version) ||
            version != 1)
        {
            problems.Add(new LegacyMigrationProblem(LegacyMigrationProblemCode.UnsupportedValue, SettingsSource, "SchemaVersion"));
        }

        bool reconnect = true;

        if (root.TryGetProperty("Reconnect", out JsonElement reconnectElement))
        {
            if (reconnectElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                reconnect = reconnectElement.GetBoolean();
            }
            else
            {
                problems.Add(new LegacyMigrationProblem(LegacyMigrationProblemCode.UnsupportedValue, SettingsSource, "Reconnect"));
            }
        }
        else
        {
            problems.Add(new LegacyMigrationProblem(LegacyMigrationProblemCode.MissingRequiredField, SettingsSource, "Reconnect"));
        }

        Dictionary<string, JsonElement> options = new(StringComparer.Ordinal);

        if (!root.TryGetProperty("Options", out JsonElement optionsElement) || optionsElement.ValueKind != JsonValueKind.Object)
        {
            problems.Add(new LegacyMigrationProblem(LegacyMigrationProblemCode.InvalidShape, SettingsSource, "Options"));
        }
        else
        {
            foreach (JsonProperty property in optionsElement.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(property.Name) || !options.TryAdd(property.Name, property.Value.Clone()))
                {
                    problems.Add(new LegacyMigrationProblem(LegacyMigrationProblemCode.UnsupportedField, SettingsSource, "Options"));
                    continue;
                }

                if (property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.True or JsonValueKind.False))
                {
                    problems.Add(new LegacyMigrationProblem(LegacyMigrationProblemCode.UnsupportedValue, SettingsSource, "Options"));
                }
            }
        }

        return problems.Count == 0 ? new MirroringPreferences { Reconnect = reconnect, Options = options } : null;
    }

    /// <summary>Parses one JSON object and records shape failures without exposing its contents.</summary>
    private static JsonDocument? ParseDocument(string json, string source, List<LegacyMigrationProblem> problems)
    {
        try
        {
            JsonDocument document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return document;
            }

            document.Dispose();
            problems.Add(new LegacyMigrationProblem(LegacyMigrationProblemCode.InvalidShape, source, "$"));
        }
        catch (JsonException)
        {
            problems.Add(new LegacyMigrationProblem(LegacyMigrationProblemCode.InvalidJson, source, "$"));
        }

        return null;
    }

    /// <summary>Refuses to drop unexpected or repeated top-level legacy fields.</summary>
    private static void ValidateProperties(JsonElement root, string source, string[] allowed, List<LegacyMigrationProblem> problems)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!allowed.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
            {
                problems.Add(new LegacyMigrationProblem(LegacyMigrationProblemCode.UnsupportedField, source, "$"));
            }
        }
    }

    /// <summary>Reads an exact JSON string or records a structured field problem.</summary>
    private static string? ReadString(JsonElement root, string name, string source, bool required, List<LegacyMigrationProblem> problems)
    {
        if (!root.TryGetProperty(name, out JsonElement element))
        {
            if (required)
            {
                problems.Add(new LegacyMigrationProblem(LegacyMigrationProblemCode.MissingRequiredField, source, name));
            }

            return null;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }

        problems.Add(new LegacyMigrationProblem(LegacyMigrationProblemCode.InvalidShape, source, name));
        return null;
    }
}
