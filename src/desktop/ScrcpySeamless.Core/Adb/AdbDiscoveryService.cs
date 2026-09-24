namespace ScrcpySeamless.Core.Adb;

/** Discovers local ADB devices and advertised wireless endpoints. */
public sealed class AdbDiscoveryService(IAdbGateway gateway)
{
    public Task<AdbResult<IReadOnlyList<AdbDevice>>> GetDevicesAsync(CancellationToken cancellationToken) =>
        gateway.GetDevicesAsync(cancellationToken);

    public async Task<AdbResult<IReadOnlyList<AdbMdnsService>>> GetServicesAsync(
        AdbServiceKind kind,
        CancellationToken cancellationToken)
    {
        AdbResult<IReadOnlyList<AdbMdnsService>> result = await gateway.GetServicesAsync(cancellationToken);

        if (!result.IsSuccess || result.Value is null)
        {
            return AdbResult<IReadOnlyList<AdbMdnsService>>.Error(result.Failure);
        }

        return AdbResult<IReadOnlyList<AdbMdnsService>>.Success(
            result.Value.Where(service => service.Kind == kind).ToArray());
    }

    /** Validates an explicitly entered endpoint without triggering ADB. */
    public static AdbResult<NetworkEndpoint> ResolveManualEndpoint(string? input)
    {
        return NetworkEndpoint.TryParse(input, out NetworkEndpoint? endpoint) && endpoint is not null
            ? AdbResult<NetworkEndpoint>.Success(endpoint)
            : AdbResult<NetworkEndpoint>.Error(AdbFailureKind.InvalidInput);
    }
}
