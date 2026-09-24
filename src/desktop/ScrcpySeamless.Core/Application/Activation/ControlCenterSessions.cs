namespace ScrcpySeamless.Core.Application.Activation;

/// <summary>Tracks distinct native sessions owned by one control-center instance.</summary>
public sealed class ControlCenterSessions
{
    private readonly Lock sync = new();
    private readonly Dictionary<SessionId, ProfileId> profilesBySession = [];
    private readonly Dictionary<ProfileId, SessionId> sessionsByProfile = [];

    /// <summary>Reserves one session for a profile without blocking on external work.</summary>
    public bool TryAdd(SessionId sessionId, ProfileId profileId)
    {
        if (sessionId.Value == Guid.Empty)
        {
            throw new ArgumentException("Session identity is required.", nameof(sessionId));
        }

        if (profileId.Value == Guid.Empty)
        {
            throw new ArgumentException("Profile identity is required.", nameof(profileId));
        }

        lock (sync)
        {
            if (profilesBySession.ContainsKey(sessionId) || sessionsByProfile.ContainsKey(profileId))
            {
                return false;
            }

            profilesBySession.Add(sessionId, profileId);
            sessionsByProfile.Add(profileId, sessionId);
            return true;
        }
    }

    /// <summary>Releases exactly the reservation associated with the supplied session.</summary>
    public bool Remove(SessionId sessionId)
    {
        lock (sync)
        {
            if (!profilesBySession.Remove(sessionId, out ProfileId profileId))
            {
                return false;
            }

            sessionsByProfile.Remove(profileId);
            return true;
        }
    }

    /// <summary>Finds the current session for a stable profile identity.</summary>
    public SessionId? Find(ProfileId profileId)
    {
        lock (sync)
        {
            return sessionsByProfile.TryGetValue(profileId, out SessionId sessionId)
                ? sessionId
                : null;
        }
    }
}
