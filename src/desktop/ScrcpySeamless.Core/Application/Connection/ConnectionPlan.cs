using System.Collections.ObjectModel;
using ScrcpySeamless.Core.Configuration;

namespace ScrcpySeamless.Core.Application.Connection;

/// <summary>Capabilities a transport must or should provide.</summary>
[Flags]
public enum ConnectionCapabilities
{
    None = 0,
    Video = 1,
    Audio = 2,
    Control = 4,
}

/// <summary>A bounded retry configuration; scheduling belongs to a later phase.</summary>
public sealed class RetryPolicy
{
    public int MaximumAttempts { get; }
    public TimeSpan InitialBackoff { get; }
    public TimeSpan MaximumBackoff { get; }

    /// <summary>Creates finite retry limits without executing any attempts.</summary>
    public RetryPolicy(int maximumAttempts, TimeSpan initialBackoff, TimeSpan maximumBackoff)
    {
        if (maximumAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        }

        if (initialBackoff < TimeSpan.Zero || maximumBackoff < initialBackoff)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBackoff));
        }

        MaximumAttempts = maximumAttempts;
        InitialBackoff = initialBackoff;
        MaximumBackoff = maximumBackoff;
    }
}

/// <summary>Optional failback constraints; automatic failback is disabled by default.</summary>
public sealed class FailbackPolicy
{
    public static FailbackPolicy Disabled { get; } = new(false, TimeSpan.Zero, TimeSpan.Zero);

    public bool Automatic { get; }
    public TimeSpan MinimumPreferredStability { get; }
    public TimeSpan MinimumSwitchInterval { get; }

    /// <summary>Requires positive hysteresis whenever automatic failback is enabled.</summary>
    public FailbackPolicy(
        bool automatic,
        TimeSpan minimumPreferredStability,
        TimeSpan minimumSwitchInterval)
    {
        bool invalidHysteresis = automatic &&
            (minimumPreferredStability <= TimeSpan.Zero || minimumSwitchInterval <= TimeSpan.Zero);

        if (invalidHysteresis || minimumPreferredStability < TimeSpan.Zero || minimumSwitchInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumPreferredStability));
        }

        Automatic = automatic;
        MinimumPreferredStability = minimumPreferredStability;
        MinimumSwitchInterval = minimumSwitchInterval;
    }
}

/// <summary>Capability and recovery requirements attached to a connection plan.</summary>
public sealed class ConnectionPolicy
{
    private const int DefaultMaximumAttempts = 3;
    private const int DefaultInitialBackoffSeconds = 1;
    private const int DefaultMaximumBackoffSeconds = 8;

    private const ConnectionCapabilities AllCapabilities =
        ConnectionCapabilities.Video | ConnectionCapabilities.Audio | ConnectionCapabilities.Control;

    public static ConnectionPolicy Default { get; } = new(
        ConnectionCapabilities.Video | ConnectionCapabilities.Control,
        AllCapabilities,
        new RetryPolicy(
            DefaultMaximumAttempts,
            TimeSpan.FromSeconds(DefaultInitialBackoffSeconds),
            TimeSpan.FromSeconds(DefaultMaximumBackoffSeconds)),
        FailbackPolicy.Disabled);

    public ConnectionCapabilities RequiredCapabilities { get; }
    public ConnectionCapabilities DesiredCapabilities { get; }
    public RetryPolicy Retry { get; }
    public FailbackPolicy Failback { get; }

    /// <summary>Ensures required capabilities are a subset of desired capabilities.</summary>
    public ConnectionPolicy(
        ConnectionCapabilities requiredCapabilities,
        ConnectionCapabilities desiredCapabilities,
        RetryPolicy retry,
        FailbackPolicy failback)
    {
        if (requiredCapabilities == ConnectionCapabilities.None ||
            (requiredCapabilities & ~desiredCapabilities) != 0 ||
            (desiredCapabilities & ~AllCapabilities) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredCapabilities));
        }

        RequiredCapabilities = requiredCapabilities;
        DesiredCapabilities = desiredCapabilities;
        Retry = retry ?? throw new ArgumentNullException(nameof(retry));
        Failback = failback ?? throw new ArgumentNullException(nameof(failback));
    }
}

/// <summary>One USB or network transport candidate for a profile.</summary>
public sealed class TransportCandidate
{
    public TransportKind Kind { get; }
    public UsbSerial? UsbSerial { get; }
    public MdnsServiceName? MdnsService { get; }
    public NetworkEndpoint? Endpoint { get; }

    private TransportCandidate(
        TransportKind kind,
        UsbSerial? usbSerial,
        MdnsServiceName? mdnsService,
        NetworkEndpoint? endpoint)
    {
        Kind = kind;
        UsbSerial = usbSerial;
        MdnsService = mdnsService;
        Endpoint = endpoint;
    }

    /// <summary>Creates a USB candidate from its current opaque ADB serial.</summary>
    public static TransportCandidate ForUsb(UsbSerial serial)
    {
        if (string.IsNullOrEmpty(serial.Value))
        {
            throw new ArgumentException("A USB candidate requires a serial.", nameof(serial));
        }

        return new TransportCandidate(TransportKind.Usb, serial, null, null);
    }

    /// <summary>Creates a network candidate resolved by mDNS and/or a saved endpoint.</summary>
    public static TransportCandidate ForNetwork(MdnsServiceName? service, NetworkEndpoint? endpoint)
    {
        if (service is { Value: null or "" } ||
            (service is null && endpoint is null))
        {
            throw new ArgumentException("A network candidate requires a service or endpoint.");
        }

        return new TransportCandidate(TransportKind.Network, null, service, endpoint);
    }
}

/// <summary>Immutable ordered transport choices, without reconnect orchestration.</summary>
public sealed class ConnectionPlan
{
    public ProfileId ProfileId { get; }
    public TransportCandidate Preferred { get; }
    public IReadOnlyList<TransportCandidate> FallbackCandidates { get; }
    public ConnectionPolicy Policy { get; }

    private ConnectionPlan(
        ProfileId profileId,
        TransportCandidate preferred,
        IReadOnlyList<TransportCandidate> fallbacks,
        ConnectionPolicy policy)
    {
        ProfileId = profileId;
        Preferred = preferred;
        FallbackCandidates = new ReadOnlyCollection<TransportCandidate>([.. fallbacks]);
        Policy = policy;
    }

    /// <summary>Builds a deterministic plan from validated saved identity and preference.</summary>
    public static bool TryCreate(
        DeviceProfile profile,
        ConnectionPolicy policy,
        out ConnectionPlan? plan,
        out ValidationIssue? issue)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(policy);
        plan = null;
        issue = profile.Validate(nameof(profile)).FirstOrDefault();

        if (issue is not null)
        {
            return false;
        }

        TransportCandidate? usb = profile.UsbIdentity is { } serial
            ? TransportCandidate.ForUsb(serial)
            : null;
        TransportCandidate? network = profile.MdnsIdentity is not null || profile.ConnectionEndpoint is not null
            ? TransportCandidate.ForNetwork(profile.MdnsIdentity, profile.ConnectionEndpoint)
            : null;

        TransportCandidate? preferred = profile.Connection.PreferredTransport switch
        {
            TransportPreference.Automatic => usb ?? network,
            TransportPreference.Usb => usb,
            TransportPreference.Network => network,
            _ => null,
        };

        if (preferred is null)
        {
            issue = new ValidationIssue(CoreErrorCode.InvalidConfiguration,
                $"{nameof(profile)}.{nameof(DeviceProfile.Connection)}.{nameof(ConnectionPreferences.PreferredTransport)}");
            return false;
        }

        List<TransportCandidate> fallbacks = [];

        if (profile.Connection.AllowFallback)
        {
            TransportCandidate? alternate = preferred.Kind == TransportKind.Usb ? network : usb;

            if (alternate is not null)
            {
                fallbacks.Add(alternate);
            }
        }

        plan = new ConnectionPlan(profile.Id, preferred, fallbacks, policy);
        return true;
    }
}
