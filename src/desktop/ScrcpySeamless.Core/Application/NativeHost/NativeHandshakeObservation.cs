namespace ScrcpySeamless.Core.Application.NativeHost;

/// <summary>Immutable accepted negotiation data with an explicit capability privacy allowlist.</summary>
public sealed record NativeHandshakeObservation
{
    public string Product { get; }
    public int ProtocolMajor { get; }
    public int ProtocolMinor { get; }
    public IReadOnlyList<string> Capabilities { get; }
    public int OmittedCapabilityCount { get; }

    /// <summary>Detaches actual negotiated values and omits arbitrary extension capability text.</summary>
    public NativeHandshakeObservation(string product, int protocolMajor, int protocolMinor,
        IReadOnlyList<string> capabilities)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(capabilities);
        Product = product;
        ProtocolMajor = protocolMajor;
        ProtocolMinor = protocolMinor;
        string[] knownCapabilities = capabilities.Where(capability =>
            capability is "focus-window" or "lifecycle-v1" or "stop").ToArray();
        Capabilities = Array.AsReadOnly(knownCapabilities);
        OmittedCapabilityCount = capabilities.Count - knownCapabilities.Length;
    }
}

/// <summary>Non-destructive access to actual machine negotiation without consuming lifecycle events.</summary>
public interface INativeHandshakeEvidence
{
    NativeHandshakeObservation? AcceptedHandshake { get; }
    int ProcessId { get; }
}
