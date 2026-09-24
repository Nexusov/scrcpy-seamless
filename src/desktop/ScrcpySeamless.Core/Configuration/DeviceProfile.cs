using System.Text.Json.Serialization;

namespace ScrcpySeamless.Core.Configuration;

/// <summary>Saved transport preference; candidate planning belongs to application policy.</summary>
public enum TransportPreference
{
    Automatic,
    Usb,
    Network,
}

/// <summary>User choices that may inform a later connection plan.</summary>
public sealed class ConnectionPreferences
{
    public TransportPreference PreferredTransport { get; init; } = TransportPreference.Automatic;
    public bool AllowFallback { get; init; } = true;

    /// <summary>Checks the saved enum without assuming a connection plan.</summary>
    public IReadOnlyList<ValidationIssue> Validate(string path)
    {
        if (!Enum.IsDefined(PreferredTransport))
        {
            return [new ValidationIssue(CoreErrorCode.InvalidConfiguration, $"{path}.{nameof(PreferredTransport)}")];
        }

        return [];
    }
}

/// <summary>A stable profile with independent device, transport, and endpoint fields.</summary>
public sealed class DeviceProfile
{
    private const int MaximumAliasLength = 128;

    [JsonRequired]
    public ProfileId Id { get; init; }
    public DeviceId? DeviceIdentity { get; init; }
    public UsbSerial? UsbIdentity { get; init; }
    public MdnsServiceName? MdnsIdentity { get; init; }
    public NetworkEndpoint? PairingEndpoint { get; init; }
    public NetworkEndpoint? ConnectionEndpoint { get; init; }
    public string? Alias { get; init; }
    public ConnectionPreferences Connection { get; init; } = new();

    /// <summary>Checks a profile without binding its stable ID to a current transport.</summary>
    public IReadOnlyList<ValidationIssue> Validate(string path)
    {
        List<ValidationIssue> issues = [];

        if (Id.Value == Guid.Empty)
        {
            issues.Add(new ValidationIssue(CoreErrorCode.InvalidIdentity, $"{path}.{nameof(Id)}"));
        }

        if (DeviceIdentity.HasValue && DeviceIdentity.Value.Value == Guid.Empty)
        {
            issues.Add(new ValidationIssue(CoreErrorCode.InvalidIdentity, $"{path}.{nameof(DeviceIdentity)}"));
        }

        if (UsbIdentity.HasValue && string.IsNullOrEmpty(UsbIdentity.Value.Value))
        {
            issues.Add(new ValidationIssue(CoreErrorCode.InvalidIdentity, $"{path}.{nameof(UsbIdentity)}"));
        }

        if (MdnsIdentity.HasValue && string.IsNullOrEmpty(MdnsIdentity.Value.Value))
        {
            issues.Add(new ValidationIssue(CoreErrorCode.InvalidIdentity, $"{path}.{nameof(MdnsIdentity)}"));
        }

        bool hasConnectionIdentity = DeviceIdentity.HasValue || UsbIdentity.HasValue || MdnsIdentity.HasValue || ConnectionEndpoint is not null;

        if (!hasConnectionIdentity)
        {
            issues.Add(new ValidationIssue(CoreErrorCode.InvalidConfiguration, path));
        }

        if (PairingEndpoint is not null && PairingEndpoint == ConnectionEndpoint)
        {
            issues.Add(new ValidationIssue(CoreErrorCode.InvalidConfiguration, $"{path}.{nameof(PairingEndpoint)}"));
        }

        if (Alias is not null &&
            (Alias.Length is < 1 or > MaximumAliasLength ||
             string.IsNullOrWhiteSpace(Alias) ||
             Alias.Any(char.IsControl)))
        {
            issues.Add(new ValidationIssue(CoreErrorCode.InvalidConfiguration, $"{path}.{nameof(Alias)}"));
        }

        if (Connection is null)
        {
            issues.Add(new ValidationIssue(CoreErrorCode.InvalidConfiguration, $"{path}.{nameof(Connection)}"));
            return issues;
        }

        issues.AddRange(Connection.Validate($"{path}.{nameof(Connection)}"));
        return issues;
    }
}
