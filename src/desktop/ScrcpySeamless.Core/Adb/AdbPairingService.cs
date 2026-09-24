namespace ScrcpySeamless.Core.Adb;

/** Performs pairing and optional connection without retaining the pairing secret. */
public sealed class AdbPairingService(IAdbGateway gateway)
{
    public async Task<AdbResult<AdbPairingOutcome>> PairAsync(
        NetworkEndpoint pairingEndpoint,
        string pairingCode,
        NetworkEndpoint? connectionEndpoint,
        string? expectedDeviceSerialProperty,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(pairingCode) || pairingCode.Length != 6
            || pairingCode.Any(character => character is < '0' or > '9'))
        {
            return AdbResult<AdbPairingOutcome>.Error(AdbFailureKind.InvalidInput);
        }

        if (connectionEndpoint is not null && connectionEndpoint.Equals(pairingEndpoint))
        {
            return AdbResult<AdbPairingOutcome>.Error(AdbFailureKind.InvalidInput);
        }

        AdbResult<bool> pairing = await gateway.PairAsync(pairingEndpoint, pairingCode, cancellationToken);

        if (!pairing.IsSuccess || pairing.Value != true)
        {
            return AdbResult<AdbPairingOutcome>.Error(
                pairing.IsSuccess ? AdbFailureKind.PairingRejected : pairing.Failure);
        }

        if (connectionEndpoint is null)
        {
            return AdbResult<AdbPairingOutcome>.Success(new AdbPairingOutcome(true, null, null));
        }

        AdbResult<bool> connection = await gateway.ConnectAsync(connectionEndpoint, cancellationToken);

        if (!connection.IsSuccess || connection.Value != true)
        {
            return AdbResult<AdbPairingOutcome>.Partial(
                new AdbPairingOutcome(true, null, null),
                connection.IsSuccess ? AdbFailureKind.ConnectionRejected : connection.Failure);
        }

        AdbResult<string> observedSerial = await gateway.GetDeviceSerialPropertyAsync(connectionEndpoint, cancellationToken);

        if (!observedSerial.IsSuccess)
        {
            return AdbResult<AdbPairingOutcome>.Partial(
                new AdbPairingOutcome(true, null, null),
                observedSerial.Failure);
        }

        if (expectedDeviceSerialProperty is not null
            && !string.Equals(expectedDeviceSerialProperty, observedSerial.Value, StringComparison.Ordinal))
        {
            return AdbResult<AdbPairingOutcome>.Partial(
                new AdbPairingOutcome(true, null, null),
                AdbFailureKind.DeviceSerialPropertyMismatch);
        }

        return AdbResult<AdbPairingOutcome>.Success(new AdbPairingOutcome(true, connectionEndpoint, observedSerial.Value));
    }
}
