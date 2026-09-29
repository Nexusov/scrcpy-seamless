using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ScrcpySeamless.Infrastructure.NativeProtocol;

/// <summary>Strict, bounded JSON representation of protocol v1 messages.</summary>
public static class ProtocolJsonCodec
{
    private const int MaximumProperties = 32;
    private const int MaximumStringBytes = 256;
    private const int MaximumCapabilityBytes = 64;
    private const int MaximumCapabilities = 16;
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly IComparer<string> Utf8CapabilityOrder = Comparer<string>.Create(
        (first, second) => StrictUtf8.GetBytes(first).AsSpan().SequenceCompareTo(
            StrictUtf8.GetBytes(second)));
    private static readonly HashSet<string> KnownFields =
    ["messageType", "product", "protocolMajor", "protocolMinor",
        "requiredCapabilities", "supportedCapabilities", "capabilities", "status",
        "requestId", "sessionId", "command", "sequence", "utc",
        "monotonicMicroseconds", "connectionAttemptId", "subsystem",
        "eventType", "reason", "error"];
    private static readonly HashSet<string> HelloStatuses =
    ["accepted", "productMismatch", "majorMismatch", "requiredCapabilityMissing"];
    private static readonly HashSet<string> CommandStatuses =
    ["accepted", "applied", "unsupportedCommand", "invalidState", "noWindow", "failed"];
    private static readonly HashSet<string> Subsystems =
    ["protocol", "native", "connection", "video", "audio", "control"];
    private static readonly HashSet<string> Events =
    ["NativeReady", "Connecting", "StreamStarted", "TransportLost",
        "ReconnectScheduled", "Reconnecting", "StreamResumed",
        "CapabilityDegraded", "SessionStopped", "FatalError"];
    private static readonly HashSet<string> AttemptEvents =
    ["Connecting", "StreamStarted", "TransportLost", "Reconnecting", "StreamResumed"];
    private static readonly HashSet<string> Reasons =
    ["none", "userStop", "windowClosed", "transportLost", "protocolError",
        "nativeFailure", "unknown"];
    private static readonly HashSet<string> Errors =
    ["none", "unknown", "invalidMessage", "unsupported", "internalFailure"];

    /// <summary>Decodes one complete JSON payload, not its frame header.</summary>
    public static ProtocolMessage Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length is 0 or > ProtocolFrameCodec.MaximumPayloadBytes)
        {
            throw new ProtocolException(ProtocolFailure.InvalidLength, "Invalid payload length.");
        }

        try
        {
            StrictUtf8.GetCharCount(payload);
        }
        catch (DecoderFallbackException)
        {
            throw new ProtocolException(ProtocolFailure.InvalidUtf8, "Invalid UTF-8 payload.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(payload.ToArray(), new JsonDocumentOptions
            {
                MaxDepth = 2,
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false,
            });

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("Root must be an object.");
            }

            Dictionary<string, JsonElement> fields = ReadFields(document.RootElement);
            string type = ReadString(fields, "messageType");
            HashSet<string> allowed = ExpectedFields(type);

            foreach (string name in fields.Keys)
            {
                if (KnownFields.Contains(name) && !allowed.Contains(name))
                {
                    throw Invalid("Field is not valid for this message type.");
                }
            }

            return type switch
            {
                "hello" => DecodeHello(fields),
                "helloResult" => DecodeHelloResult(fields),
                "command" => DecodeCommand(fields),
                "commandResult" => DecodeCommandResult(fields),
                "lifecycle" => DecodeLifecycle(fields),
                _ => throw new ProtocolException(ProtocolFailure.UnsupportedMessage,
                    "Unsupported message type."),
            };
        }
        catch (JsonException)
        {
            throw new ProtocolException(ProtocolFailure.InvalidJson, "Invalid JSON payload.");
        }
        catch (InvalidOperationException)
        {
            throw new ProtocolException(ProtocolFailure.InvalidJson, "Invalid JSON string encoding.");
        }
        catch (EncoderFallbackException)
        {
            throw new ProtocolException(ProtocolFailure.InvalidUtf8, "Invalid JSON string encoding.");
        }
    }

    /// <summary>Encodes a message in deterministic field order.</summary>
    public static byte[] Encode(ProtocolMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ValidateForEncoding(message);
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            writer.WriteStartObject();
            writer.WriteString("messageType", message.MessageType);

            switch (message.MessageType)
            {
                case "hello":
                    WriteVersion(writer, message);
                    WriteCapabilities(writer, "requiredCapabilities", message.RequiredCapabilities);
                    WriteCapabilities(writer, "supportedCapabilities", message.SupportedCapabilities);
                    break;
                case "helloResult":
                    WriteVersion(writer, message);
                    writer.WriteString("status", message.Status);
                    WriteCapabilities(writer, "capabilities", message.Capabilities);
                    break;
                case "command":
                    WriteRequest(writer, message);
                    break;
                case "commandResult":
                    WriteRequest(writer, message);
                    writer.WriteString("status", message.Status);
                    break;
                case "lifecycle":
                    writer.WriteString("sequence", Decimal(message.Sequence));
                    writer.WriteString("utc", message.Utc?.UtcDateTime.ToString(
                        TimestampFormat, CultureInfo.InvariantCulture));
                    writer.WriteString("monotonicMicroseconds", Decimal(message.MonotonicMicroseconds));
                    writer.WriteString("sessionId", message.SessionId?.ToString("D"));
                    if (message.ConnectionAttemptId is null)
                    {
                        writer.WriteNull("connectionAttemptId");
                    }
                    else
                    {
                        writer.WriteString("connectionAttemptId",
                            message.ConnectionAttemptId.Value.ToString("D"));
                    }
                    writer.WriteString("subsystem", message.Subsystem);
                    writer.WriteString("eventType", message.EventType);
                    writer.WriteString("reason", message.Reason);
                    writer.WriteString("error", message.Error);
                    break;
                default:
                    throw new ProtocolException(ProtocolFailure.UnsupportedMessage,
                        "Unsupported message type.");
            }

            writer.WriteEndObject();
        }

        byte[] payload = stream.ToArray();
        _ = Decode(payload);
        return payload;
    }

    /// <summary>Bounds public message fields before the JSON writer allocates output.</summary>
    private static void ValidateForEncoding(ProtocolMessage message)
    {
        string?[] ordinaryStrings =
        [message.MessageType, message.Product, message.Status, message.Command,
            message.Subsystem, message.EventType, message.Reason, message.Error];

        foreach (string? value in ordinaryStrings)
        {
            if (value is not null)
            {
                _ = CheckedString(value, MaximumStringBytes);
            }
        }

        IReadOnlyList<string>?[] capabilityArrays =
        [message.RequiredCapabilities, message.SupportedCapabilities, message.Capabilities];

        foreach (IReadOnlyList<string>? capabilities in capabilityArrays)
        {
            if (capabilities is null)
            {
                continue;
            }

            if (capabilities.Count > MaximumCapabilities)
            {
                throw Invalid("Too many capabilities.");
            }

            foreach (string capability in capabilities)
            {
                if (capability is null)
                {
                    throw Invalid("Null capability.");
                }

                _ = CheckedString(capability, MaximumCapabilityBytes);
            }
        }
    }

    /// <summary>Validates root shape, duplicate keys and unknown optional values.</summary>
    private static Dictionary<string, JsonElement> ReadFields(JsonElement root)
    {
        Dictionary<string, JsonElement> fields = new(StringComparer.Ordinal);

        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (fields.Count == MaximumProperties ||
                !fields.TryAdd(CheckedString(property.Name, MaximumStringBytes), property.Value))
            {
                throw Invalid("Too many or duplicate properties.");
            }

            if (!KnownFields.Contains(property.Name))
            {
                if (property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                {
                    throw Invalid("Unknown nested field.");
                }

                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    _ = CheckedString(property.Value.GetString()!, MaximumStringBytes,
                        allowEmpty: true);
                }

                if (property.Value.ValueKind == JsonValueKind.Number &&
                    (!property.Value.TryGetDouble(out double number) || !double.IsFinite(number)))
                {
                    throw Invalid("Unknown numeric field is outside the finite binary64 range.");
                }
            }
        }

        return fields;
    }

    /// <summary>Returns fields permitted for one known message type.</summary>
    private static HashSet<string> ExpectedFields(string type) => type switch
    {
        "hello" => ["messageType", "product", "protocolMajor", "protocolMinor",
            "requiredCapabilities", "supportedCapabilities"],
        "helloResult" => ["messageType", "product", "protocolMajor", "protocolMinor",
            "status", "capabilities"],
        "command" => ["messageType", "requestId", "sessionId", "command"],
        "commandResult" => ["messageType", "requestId", "sessionId", "command", "status"],
        "lifecycle" => ["messageType", "sequence", "utc", "monotonicMicroseconds",
            "sessionId", "connectionAttemptId", "subsystem", "eventType", "reason", "error"],
        _ => throw new ProtocolException(ProtocolFailure.UnsupportedMessage,
            "Unsupported message type."),
    };

    /// <summary>Decodes a protocol proposal.</summary>
    private static ProtocolMessage DecodeHello(Dictionary<string, JsonElement> fields)
    {
        IReadOnlyList<string> required = ReadCapabilities(fields, "requiredCapabilities");
        IReadOnlyList<string> supported = ReadCapabilities(fields, "supportedCapabilities");

        if (required.Any(capability => !supported.Contains(capability, StringComparer.Ordinal)))
        {
            throw Invalid("Required capability is absent from supported capabilities.");
        }

        return new ProtocolMessage
        {
            MessageType = "hello",
            Product = ReadString(fields, "product"),
            ProtocolMajor = ReadVersion(fields, "protocolMajor"),
            ProtocolMinor = ReadVersion(fields, "protocolMinor"),
            RequiredCapabilities = required,
            SupportedCapabilities = supported,
        };
    }

    /// <summary>Decodes an explicit handshake decision.</summary>
    private static ProtocolMessage DecodeHelloResult(Dictionary<string, JsonElement> fields)
    {
        string status = ReadEnum(fields, "status", HelloStatuses);
        IReadOnlyList<string> capabilities = ReadCapabilities(fields, "capabilities");

        if (status != "accepted" && capabilities.Count != 0)
        {
            throw Invalid("Rejected hello cannot advertise negotiated capabilities.");
        }

        return new ProtocolMessage
        {
            MessageType = "helloResult",
            Product = ReadString(fields, "product"),
            ProtocolMajor = ReadVersion(fields, "protocolMajor"),
            ProtocolMinor = ReadVersion(fields, "protocolMinor"),
            Status = status,
            Capabilities = capabilities,
        };
    }

    /// <summary>Decodes a command while retaining an unknown name for a typed rejection.</summary>
    private static ProtocolMessage DecodeCommand(Dictionary<string, JsonElement> fields) => new()
    {
        MessageType = "command",
        RequestId = ReadDecimal(fields, "requestId", allowZero: false),
        SessionId = ReadGuid(fields, "sessionId"),
        Command = ReadString(fields, "command"),
    };

    /// <summary>Decodes a correlated command result.</summary>
    private static ProtocolMessage DecodeCommandResult(Dictionary<string, JsonElement> fields) => new()
    {
        MessageType = "commandResult",
        RequestId = ReadDecimal(fields, "requestId", allowZero: false),
        SessionId = ReadGuid(fields, "sessionId"),
        Command = ReadString(fields, "command"),
        Status = ReadEnum(fields, "status", CommandStatuses),
    };

    /// <summary>Decodes one ordered native observation.</summary>
    private static ProtocolMessage DecodeLifecycle(Dictionary<string, JsonElement> fields)
    {
        string eventType = ReadEnum(fields, "eventType", Events);
        Guid? attempt = ReadNullableGuid(fields, "connectionAttemptId");

        if (AttemptEvents.Contains(eventType) && attempt is null)
        {
            throw Invalid("Event requires a connection attempt.");
        }

        return new ProtocolMessage
        {
            MessageType = "lifecycle",
            Sequence = ReadDecimal(fields, "sequence", allowZero: false),
            Utc = ReadUtc(fields),
            MonotonicMicroseconds = ReadDecimal(fields, "monotonicMicroseconds", allowZero: true),
            SessionId = ReadGuid(fields, "sessionId"),
            ConnectionAttemptId = attempt,
            Subsystem = ReadEnum(fields, "subsystem", Subsystems),
            EventType = eventType,
            Reason = ReadEnum(fields, "reason", Reasons),
            Error = ReadEnum(fields, "error", Errors),
        };
    }

    /// <summary>Reads a required nonempty bounded JSON string.</summary>
    private static string ReadString(Dictionary<string, JsonElement> fields, string name)
    {
        if (!fields.TryGetValue(name, out JsonElement value) ||
            value.ValueKind != JsonValueKind.String)
        {
            throw Invalid("Missing or invalid required string.");
        }

        return CheckedString(value.GetString()!, MaximumStringBytes);
    }

    /// <summary>Checks decoded text without depending on C string terminators.</summary>
    private static string CheckedString(string value, int maximumBytes,
        bool allowEmpty = false)
    {
        if ((!allowEmpty && value.Length == 0) || value.Length > maximumBytes ||
            value.Contains('\0') ||
            StrictUtf8.GetByteCount(value) > maximumBytes)
        {
            throw Invalid("Invalid string length or embedded NUL.");
        }

        return value;
    }

    /// <summary>Reads one token from an explicit known vocabulary.</summary>
    private static string ReadEnum(Dictionary<string, JsonElement> fields, string name,
        HashSet<string> values)
    {
        string value = ReadString(fields, name);

        if (!values.Contains(value))
        {
            throw Invalid("Unsupported enum value.");
        }

        return value;
    }

    /// <summary>Reads a bounded version integer without floating-point conversion.</summary>
    private static int ReadVersion(Dictionary<string, JsonElement> fields, string name)
    {
        if (!fields.TryGetValue(name, out JsonElement value) ||
            value.ValueKind != JsonValueKind.Number)
        {
            throw Invalid("Invalid protocol version.");
        }

        string token = value.GetRawText();

        if (token.Length is < 1 or > 5 ||
            token.Any(character => character is < '0' or > '9') ||
            !value.TryGetInt32(out int version) ||
            version is < 0 or > 65535)
        {
            throw Invalid("Invalid protocol version.");
        }

        return version;
    }

    /// <summary>Reads a canonical decimal string with exact uint64 precision.</summary>
    private static ulong ReadDecimal(Dictionary<string, JsonElement> fields, string name,
        bool allowZero)
    {
        string value = ReadString(fields, name);

        if ((value.Length > 1 && value[0] == '0') ||
            value.Any(character => character is < '0' or > '9') ||
            !ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong number) ||
            (!allowZero && number == 0))
        {
            throw Invalid("Invalid decimal identifier.");
        }

        return number;
    }

    /// <summary>Reads a nonzero lowercase GUID D string.</summary>
    private static Guid ReadGuid(Dictionary<string, JsonElement> fields, string name)
    {
        string value = ReadString(fields, name);

        if (!Guid.TryParseExact(value, "D", out Guid identity) ||
            identity == Guid.Empty || value != identity.ToString("D"))
        {
            throw Invalid("Invalid identity.");
        }

        return identity;
    }

    /// <summary>Reads the only explicitly nullable field.</summary>
    private static Guid? ReadNullableGuid(Dictionary<string, JsonElement> fields, string name)
    {
        if (!fields.TryGetValue(name, out JsonElement value))
        {
            throw Invalid("Missing connection attempt field.");
        }

        return value.ValueKind == JsonValueKind.Null ? null : ReadGuid(fields, name);
    }

    /// <summary>Reads an exact UTC millisecond timestamp.</summary>
    private static DateTimeOffset ReadUtc(Dictionary<string, JsonElement> fields)
    {
        string value = ReadString(fields, "utc");

        if (value.Length != 24 || !DateTimeOffset.TryParseExact(value, TimestampFormat,
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset utc))
        {
            throw Invalid("Invalid UTC timestamp.");
        }

        return utc.ToUniversalTime();
    }

    /// <summary>Reads a bounded set of unique capability tokens.</summary>
    private static IReadOnlyList<string> ReadCapabilities(
        Dictionary<string, JsonElement> fields, string name)
    {
        if (!fields.TryGetValue(name, out JsonElement value) ||
            value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > MaximumCapabilities)
        {
            throw Invalid("Invalid capabilities array.");
        }

        List<string> capabilities = [];
        HashSet<string> unique = new(StringComparer.Ordinal);

        foreach (JsonElement element in value.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                throw Invalid("Invalid capability type.");
            }

            string capability = CheckedString(element.GetString()!, MaximumCapabilityBytes);

            if (!unique.Add(capability))
            {
                throw Invalid("Duplicate capability.");
            }

            capabilities.Add(capability);
        }

        return capabilities;
    }

    /// <summary>Writes the shared handshake identity and version fields.</summary>
    private static void WriteVersion(Utf8JsonWriter writer, ProtocolMessage message)
    {
        writer.WriteString("product", message.Product);
        writer.WriteNumber("protocolMajor", message.ProtocolMajor ?? -1);
        writer.WriteNumber("protocolMinor", message.ProtocolMinor ?? -1);
    }

    /// <summary>Writes sorted capability names in stable order.</summary>
    private static void WriteCapabilities(Utf8JsonWriter writer, string name,
        IReadOnlyList<string>? capabilities)
    {
        writer.WriteStartArray(name);

        if (capabilities is not null)
        {
            foreach (string capability in capabilities.Order(Utf8CapabilityOrder))
            {
                writer.WriteStringValue(capability);
            }
        }

        writer.WriteEndArray();
    }

    /// <summary>Writes the shared command correlation fields.</summary>
    private static void WriteRequest(Utf8JsonWriter writer, ProtocolMessage message)
    {
        writer.WriteString("requestId", Decimal(message.RequestId));
        writer.WriteString("sessionId", message.SessionId?.ToString("D"));
        writer.WriteString("command", message.Command);
    }

    /// <summary>Formats an exact unsigned wire integer.</summary>
    private static string Decimal(ulong? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? throw Invalid("Missing decimal field.");

    /// <summary>Creates a semantic protocol error without echoing untrusted values.</summary>
    private static ProtocolException Invalid(string message) =>
        new(ProtocolFailure.InvalidMessage, message);
}
