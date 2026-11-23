namespace ScrcpySeamless.Core.Application.Updates;

public enum ReleaseChannel
{
    Stable,
    Prerelease,
}

public enum ReleaseCheckStatus
{
    UpdateAvailable,
    NoNewerRelease,
    NoEligibleRelease,
    InvalidRequest,
    Offline,
    TimedOut,
    Cancelled,
    RateLimited,
    Unavailable,
    InvalidResponse,
    ResponseTooLarge,
}

/// <summary>The caller supplies its actual product version and an explicit channel.</summary>
public sealed record ReleaseCheckRequest(string CurrentVersion, ReleaseChannel Channel);

/// <summary>Only validated version text and a constructed trusted release URL leave the adapter.</summary>
public sealed record ReleaseCheckResult(
    ReleaseCheckStatus Status,
    string? Version = null,
    Uri? ReleaseUri = null);

/// <summary>One user-initiated check; creation of an implementation performs no network work.</summary>
public interface IReleaseChecker
{
    Task<ReleaseCheckResult> CheckAsync(ReleaseCheckRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Stable excludes both semantic prereleases and releases marked prerelease by the provider.</summary>
public static class ReleaseChannelPolicy
{
    public static bool Includes(SemanticVersion version, bool providerPrerelease, ReleaseChannel channel) => channel switch
    {
        ReleaseChannel.Stable => !version.IsPrerelease && !providerPrerelease,
        ReleaseChannel.Prerelease => true,
        _ => false,
    };
}
