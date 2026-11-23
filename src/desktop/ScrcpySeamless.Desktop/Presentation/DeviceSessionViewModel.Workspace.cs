using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Application.Activation;
using ScrcpySeamless.Core.Application.NativeHost;

namespace ScrcpySeamless.Desktop.Presentation;

public sealed partial class DeviceSessionViewModel
{
    private ControlCenterSessions? ownershipRegistry;
    private SessionId? reservedSession;
    private string? capturedProfileLabel;

    public string SessionLabel => capturedProfileLabel ?? SelectedLaunchProfile?.Label ?? text.Get("sessions.newEntry");

    internal void AttachOwnershipRegistry(ControlCenterSessions registry)
    {
        if (ownershipRegistry is not null || HasOwnedSession)
        {
            throw new InvalidOperationException("Session ownership must be attached before launch.");
        }

        ownershipRegistry = registry;
    }

    private bool ReserveLaunch(NativeStartRequest request, string profileLabel)
    {
        if (ownershipRegistry?.TryAdd(request.SessionId, request.ProfileId) == false)
        {
            return false;
        }

        lock (gate)
        {
            reservedSession = request.SessionId;
            capturedProfileLabel = profileLabel;
        }

        OnPropertyChanged(nameof(SessionLabel));
        return true;
    }

    private void ReleaseLaunch(SessionId expected)
    {
        lock (gate)
        {
            if (reservedSession != expected)
            {
                return;
            }

            reservedSession = null;
        }

        ownershipRegistry?.Remove(expected);
        OnPropertyChanged(nameof(HasOwnedSession));
    }
}
