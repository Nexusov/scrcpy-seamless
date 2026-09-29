namespace ScrcpySeamless.Infrastructure.NativeProtocol;

/// <summary>Stable protocol failure classes without raw input in diagnostics.</summary>
public enum ProtocolFailure
{
    InvalidLength,
    TruncatedHeader,
    TruncatedPayload,
    InvalidUtf8,
    InvalidJson,
    InvalidMessage,
    UnsupportedMessage,
}

/// <summary>Reports a bounded machine-protocol failure.</summary>
public sealed class ProtocolException(ProtocolFailure failure, string message) : Exception(message)
{
    public ProtocolFailure Failure { get; } = failure;
}

/// <summary>One decoded wire message; irrelevant fields are null for its type.</summary>
public sealed record ProtocolMessage
{
    public required string MessageType { get; init; }
    public string? Product { get; init; }
    public int? ProtocolMajor { get; init; }
    public int? ProtocolMinor { get; init; }
    public IReadOnlyList<string>? RequiredCapabilities { get; init; }
    public IReadOnlyList<string>? SupportedCapabilities { get; init; }
    public IReadOnlyList<string>? Capabilities { get; init; }
    public string? Status { get; init; }
    public ulong? RequestId { get; init; }
    public Guid? SessionId { get; init; }
    public string? Command { get; init; }
    public ulong? Sequence { get; init; }
    public DateTimeOffset? Utc { get; init; }
    public ulong? MonotonicMicroseconds { get; init; }
    public Guid? ConnectionAttemptId { get; init; }
    public string? Subsystem { get; init; }
    public string? EventType { get; init; }
    public string? Reason { get; init; }
    public string? Error { get; init; }
}

/// <summary>Capability negotiation independent of native process execution.</summary>
public static class ProtocolCompatibility
{
    public const string Product = "scrcpy-seamless";
    public const int Major = 1;
    public const int Minor = 0;

    public static IReadOnlyList<string> RequiredCapabilities { get; } =
        Array.AsReadOnly(new[] { "focus-window", "lifecycle-v1", "stop" });

    private static readonly HashSet<string> Supported = new(RequiredCapabilities, StringComparer.Ordinal);

    /// <summary>Evaluates a decoded client hello without starting a session.</summary>
    public static ProtocolMessage Evaluate(ProtocolMessage hello)
    {
        if (hello.MessageType != "hello")
        {
            throw new ProtocolException(ProtocolFailure.InvalidMessage, "Expected hello.");
        }

        if (hello.RequiredCapabilities is null || hello.SupportedCapabilities is null ||
            hello.RequiredCapabilities.Any(capability =>
                !hello.SupportedCapabilities.Contains(capability, StringComparer.Ordinal)))
        {
            throw new ProtocolException(ProtocolFailure.InvalidMessage,
                "Contradictory hello capabilities.");
        }

        string status = hello.Product != Product ? "productMismatch" :
            hello.ProtocolMajor != Major ? "majorMismatch" :
            hello.RequiredCapabilities!.Any(capability => !Supported.Contains(capability))
                ? "requiredCapabilityMissing" : "accepted";

        return new ProtocolMessage
        {
            MessageType = "helloResult",
            Product = Product,
            ProtocolMajor = Major,
            ProtocolMinor = Math.Min(hello.ProtocolMinor!.Value, Minor),
            Status = status,
            Capabilities = status == "accepted"
                ? hello.SupportedCapabilities!.Where(Supported.Contains)
                    .Order(StringComparer.Ordinal).ToArray()
                : [],
        };
    }
}
