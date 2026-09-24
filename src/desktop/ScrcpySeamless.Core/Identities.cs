using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScrcpySeamless.Core;

/// <summary>Stable saved profile identity, independent of any discovered transport.</summary>
[JsonConverter(typeof(ProfileIdJsonConverter))]
public readonly record struct ProfileId
{
    public Guid Value { get; }

    /// <summary>Creates a nonempty profile identity.</summary>
    public ProfileId(Guid value)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(value, Guid.Empty);
        Value = value;
    }

    /// <summary>Allocates a new profile identity.</summary>
    public static ProfileId New() => new(Guid.NewGuid());

    /// <summary>Formats the identity consistently across cultures.</summary>
    public override string ToString() => Value.ToString("D");
}

/// <summary>Application-assigned device reference, not proof of physical identity.</summary>
[JsonConverter(typeof(DeviceIdJsonConverter))]
public readonly record struct DeviceId
{
    public Guid Value { get; }

    /// <summary>Creates a nonempty device identity.</summary>
    public DeviceId(Guid value)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(value, Guid.Empty);
        Value = value;
    }

    /// <summary>Allocates a new device identity.</summary>
    public static DeviceId New() => new(Guid.NewGuid());

    /// <summary>Formats the identity consistently across cultures.</summary>
    public override string ToString() => Value.ToString("D");
}

/// <summary>Identity of one application-owned mirroring session.</summary>
[JsonConverter(typeof(SessionIdJsonConverter))]
public readonly record struct SessionId
{
    public Guid Value { get; }

    /// <summary>Creates a nonempty session identity.</summary>
    public SessionId(Guid value)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(value, Guid.Empty);
        Value = value;
    }

    /// <summary>Allocates a new session identity.</summary>
    public static SessionId New() => new(Guid.NewGuid());

    /// <summary>Formats the identity consistently across cultures.</summary>
    public override string ToString() => Value.ToString("D");
}

/// <summary>Identity of one connection attempt within a session.</summary>
[JsonConverter(typeof(ConnectionAttemptIdJsonConverter))]
public readonly record struct ConnectionAttemptId
{
    public Guid Value { get; }

    /// <summary>Creates a nonempty attempt identity.</summary>
    public ConnectionAttemptId(Guid value)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(value, Guid.Empty);
        Value = value;
    }

    /// <summary>Allocates a new attempt identity.</summary>
    public static ConnectionAttemptId New() => new(Guid.NewGuid());

    /// <summary>Formats the identity consistently across cultures.</summary>
    public override string ToString() => Value.ToString("D");
}

/// <summary>Opaque USB ADB serial; it is not a permanent physical-device identity.</summary>
[JsonConverter(typeof(UsbSerialJsonConverter))]
public readonly record struct UsbSerial
{
    public string Value { get; }

    /// <summary>Preserves a nonempty external serial without normalizing it.</summary>
    public UsbSerial(string value)
    {
        ExternalIdentity.Validate(value);
        Value = value;
    }

    /// <summary>Returns the exact external serial.</summary>
    public override string ToString() => Value;
}

/// <summary>Opaque discovered mDNS service name, separate from a connect endpoint.</summary>
[JsonConverter(typeof(MdnsServiceNameJsonConverter))]
public readonly record struct MdnsServiceName
{
    public string Value { get; }

    /// <summary>Preserves a nonempty external service name without normalizing it.</summary>
    public MdnsServiceName(string value)
    {
        ExternalIdentity.Validate(value);
        Value = value;
    }

    /// <summary>Returns the exact external service name.</summary>
    public override string ToString() => Value;
}

internal static class ExternalIdentity
{
    /// <summary>Rejects only empty and control-bearing external identifiers.</summary>
    internal static void Validate(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Any(char.IsControl))
        {
            throw new ArgumentException("External identity contains a control character.", nameof(value));
        }
    }
}

internal static class IdentityJson
{
    /// <summary>Reads a nonempty canonical GUID from a JSON string.</summary>
    internal static Guid ReadGuid(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.String ||
            !Guid.TryParseExact(reader.GetString(), "D", out Guid value) ||
            value == Guid.Empty)
        {
            throw new JsonException("Invalid identity.");
        }

        return value;
    }

    /// <summary>Writes a nonempty GUID in its invariant form.</summary>
    internal static void WriteGuid(Utf8JsonWriter writer, Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new JsonException("Invalid identity.");
        }

        writer.WriteStringValue(value.ToString("D"));
    }

    /// <summary>Reads an opaque external identity without normalizing its bytes.</summary>
    internal static string ReadExternal(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Invalid external identity.");
        }

        string? value = reader.GetString();

        try
        {
            ExternalIdentity.Validate(value!);
        }
        catch (ArgumentException)
        {
            throw new JsonException("Invalid external identity.");
        }

        return value!;
    }

    /// <summary>Writes an opaque external identity only when it is valid.</summary>
    internal static void WriteExternal(Utf8JsonWriter writer, string value)
    {
        try
        {
            ExternalIdentity.Validate(value);
        }
        catch (ArgumentException)
        {
            throw new JsonException("Invalid external identity.");
        }

        writer.WriteStringValue(value);
    }
}

/// <summary>Serializes a profile ID as a GUID string.</summary>
public sealed class ProfileIdJsonConverter : JsonConverter<ProfileId>
{
    /// <summary>Reads a validated profile ID.</summary>
    public override ProfileId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(IdentityJson.ReadGuid(ref reader));

    /// <summary>Writes a validated profile ID.</summary>
    public override void Write(Utf8JsonWriter writer, ProfileId value, JsonSerializerOptions options) => IdentityJson.WriteGuid(writer, value.Value);
}

/// <summary>Serializes a device ID as a GUID string.</summary>
public sealed class DeviceIdJsonConverter : JsonConverter<DeviceId>
{
    /// <summary>Reads a validated device ID.</summary>
    public override DeviceId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(IdentityJson.ReadGuid(ref reader));

    /// <summary>Writes a validated device ID.</summary>
    public override void Write(Utf8JsonWriter writer, DeviceId value, JsonSerializerOptions options) => IdentityJson.WriteGuid(writer, value.Value);
}

/// <summary>Serializes a session ID as a GUID string.</summary>
public sealed class SessionIdJsonConverter : JsonConverter<SessionId>
{
    /// <summary>Reads a validated session ID.</summary>
    public override SessionId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(IdentityJson.ReadGuid(ref reader));

    /// <summary>Writes a validated session ID.</summary>
    public override void Write(Utf8JsonWriter writer, SessionId value, JsonSerializerOptions options) => IdentityJson.WriteGuid(writer, value.Value);
}

/// <summary>Serializes an attempt ID as a GUID string.</summary>
public sealed class ConnectionAttemptIdJsonConverter : JsonConverter<ConnectionAttemptId>
{
    /// <summary>Reads a validated attempt ID.</summary>
    public override ConnectionAttemptId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(IdentityJson.ReadGuid(ref reader));

    /// <summary>Writes a validated attempt ID.</summary>
    public override void Write(Utf8JsonWriter writer, ConnectionAttemptId value, JsonSerializerOptions options) => IdentityJson.WriteGuid(writer, value.Value);
}

/// <summary>Serializes an opaque USB serial as a JSON string.</summary>
public sealed class UsbSerialJsonConverter : JsonConverter<UsbSerial>
{
    /// <summary>Reads a validated USB serial.</summary>
    public override UsbSerial Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(IdentityJson.ReadExternal(ref reader));

    /// <summary>Writes a validated USB serial.</summary>
    public override void Write(Utf8JsonWriter writer, UsbSerial value, JsonSerializerOptions options) => IdentityJson.WriteExternal(writer, value.Value);
}

/// <summary>Serializes an opaque mDNS service name as a JSON string.</summary>
public sealed class MdnsServiceNameJsonConverter : JsonConverter<MdnsServiceName>
{
    /// <summary>Reads a validated mDNS service name.</summary>
    public override MdnsServiceName Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(IdentityJson.ReadExternal(ref reader));

    /// <summary>Writes a validated mDNS service name.</summary>
    public override void Write(Utf8JsonWriter writer, MdnsServiceName value, JsonSerializerOptions options) => IdentityJson.WriteExternal(writer, value.Value);
}
