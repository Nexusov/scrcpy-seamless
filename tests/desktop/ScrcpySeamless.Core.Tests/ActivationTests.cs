using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Application.Activation;
using Xunit;

namespace ScrcpySeamless.Core.Tests;

/// <summary>Protects one-control-center and multiple-owned-session semantics.</summary>
public sealed class ActivationTests
{
    private const int ConcurrentReservationCount = 32;

    /// <summary>One profile has at most one session, while different profiles coexist.</summary>
    [Fact]
    public void SeparateProfilesCanOwnConcurrentSessions()
    {
        ControlCenterSessions sessions = new();
        ProfileId firstProfile = ProfileId.New();
        ProfileId secondProfile = ProfileId.New();
        SessionId firstSession = SessionId.New();
        SessionId secondSession = SessionId.New();

        Assert.True(sessions.TryAdd(firstSession, firstProfile));
        Assert.True(sessions.TryAdd(secondSession, secondProfile));
        Assert.False(sessions.TryAdd(SessionId.New(), firstProfile));
        Assert.False(sessions.TryAdd(firstSession, ProfileId.New()));
        Assert.Equal(firstSession, sessions.Find(firstProfile));
        Assert.Equal(secondSession, sessions.Find(secondProfile));

        Assert.True(sessions.Remove(firstSession));
        Assert.Null(sessions.Find(firstProfile));
        Assert.Equal(secondSession, sessions.Find(secondProfile));
    }

    /// <summary>Repeated app activation and session focus require distinct request shapes.</summary>
    [Fact]
    public void ActivationIntentRequiresMatchingTarget()
    {
        Assert.Throws<ArgumentException>(() => new ActivationRequest(ActivationKind.FocusSession));
        Assert.Throws<ArgumentException>(() => new ActivationRequest(ActivationKind.ShowControlCenter, SessionId.New()));

        ActivationRequest show = new(ActivationKind.ShowControlCenter);
        ActivationRequest focus = new(ActivationKind.FocusSession, SessionId.New());

        Assert.Null(show.SessionId);
        Assert.NotNull(focus.SessionId);
    }

    /// <summary>Simultaneous reservations cannot create two sessions for one profile.</summary>
    [Fact]
    public void ConcurrentReservationsKeepProfileUnique()
    {
        ControlCenterSessions sessions = new();
        ProfileId profileId = ProfileId.New();
        int successfulReservations = 0;

        Parallel.For(0, ConcurrentReservationCount, _ =>
        {
            if (sessions.TryAdd(SessionId.New(), profileId))
            {
                Interlocked.Increment(ref successfulReservations);
            }
        });

        Assert.Equal(1, successfulReservations);
        Assert.NotNull(sessions.Find(profileId));
    }
}
