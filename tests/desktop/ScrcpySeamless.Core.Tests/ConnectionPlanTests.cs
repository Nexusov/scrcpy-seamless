using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Application.Connection;
using ScrcpySeamless.Core.Configuration;
using Xunit;

namespace ScrcpySeamless.Core.Tests;

/// <summary>Protects headless transport planning without starting ADB or native processes.</summary>
public sealed class ConnectionPlanTests
{
    /// <summary>Combined automatic mode prefers USB and retains network fallback.</summary>
    [Fact]
    public void AutomaticCombinedModePrefersUsbWithNetworkFallback()
    {
        DeviceProfile profile = new()
        {
            Id = ProfileId.New(),
            UsbIdentity = new UsbSerial("SERIAL_SYNTHETIC"),
            MdnsIdentity = new MdnsServiceName("adb-synthetic._adb-tls-connect._tcp"),
            ConnectionEndpoint = NetworkEndpoint.Parse("phone.local:5555"),
        };

        bool created = ConnectionPlan.TryCreate(profile, ConnectionPolicy.Default, out ConnectionPlan? plan, out ValidationIssue? issue);

        Assert.True(created);
        Assert.Null(issue);
        Assert.NotNull(plan);
        Assert.Equal(profile.Id, plan.ProfileId);
        Assert.Equal(TransportKind.Usb, plan.Preferred.Kind);
        TransportCandidate fallback = Assert.Single(plan.FallbackCandidates);
        Assert.Equal(TransportKind.Network, fallback.Kind);
        Assert.Equal(profile.MdnsIdentity, fallback.MdnsService);
        Assert.Equal(profile.ConnectionEndpoint, fallback.Endpoint);
    }

    /// <summary>An explicit network preference makes USB the optional fallback.</summary>
    [Fact]
    public void NetworkPreferenceReordersCandidates()
    {
        DeviceProfile profile = new()
        {
            Id = ProfileId.New(),
            UsbIdentity = new UsbSerial("SERIAL_SYNTHETIC"),
            ConnectionEndpoint = NetworkEndpoint.Parse("[2001:db8::5]:5555"),
            Connection = new ConnectionPreferences { PreferredTransport = TransportPreference.Network },
        };

        bool created = ConnectionPlan.TryCreate(profile, ConnectionPolicy.Default, out ConnectionPlan? plan, out ValidationIssue? issue);

        Assert.True(created);
        Assert.Null(issue);
        Assert.NotNull(plan);
        Assert.Equal(TransportKind.Network, plan.Preferred.Kind);
        Assert.Equal(TransportKind.Usb, Assert.Single(plan.FallbackCandidates).Kind);
    }

    /// <summary>Automatic mode works with a network-only profile without inventing USB.</summary>
    [Fact]
    public void NetworkOnlyProfileHasNoUsbFallback()
    {
        DeviceProfile profile = new()
        {
            Id = ProfileId.New(),
            MdnsIdentity = new MdnsServiceName("adb-synthetic._adb-tls-connect._tcp"),
        };

        Assert.True(ConnectionPlan.TryCreate(profile, ConnectionPolicy.Default, out ConnectionPlan? plan, out ValidationIssue? issue));
        Assert.Null(issue);
        Assert.NotNull(plan);
        Assert.Equal(TransportKind.Network, plan.Preferred.Kind);
        Assert.Empty(plan.FallbackCandidates);
    }

    /// <summary>An application device reference alone cannot invent a reachable transport.</summary>
    [Fact]
    public void DeviceIdOnlyProfileRemainsSavedButHasNoConnectionPlan()
    {
        DeviceProfile profile = new()
        {
            Id = ProfileId.New(),
            DeviceIdentity = DeviceId.New(),
        };

        Assert.Empty(profile.Validate("profile"));
        Assert.False(ConnectionPlan.TryCreate(profile, ConnectionPolicy.Default, out ConnectionPlan? plan, out ValidationIssue? issue));
        Assert.Null(plan);
        Assert.Equal(CoreErrorCode.InvalidConfiguration, issue?.Code);
    }

    /// <summary>Disabling fallback never manufactures an alternate candidate.</summary>
    [Fact]
    public void FallbackCanBeDisabled()
    {
        DeviceProfile profile = new()
        {
            Id = ProfileId.New(),
            UsbIdentity = new UsbSerial("SERIAL_SYNTHETIC"),
            ConnectionEndpoint = NetworkEndpoint.Parse("192.0.2.8:5555"),
            Connection = new ConnectionPreferences { AllowFallback = false },
        };

        Assert.True(ConnectionPlan.TryCreate(profile, ConnectionPolicy.Default, out ConnectionPlan? plan, out ValidationIssue? issue));
        Assert.Null(issue);
        Assert.NotNull(plan);
        Assert.Empty(plan.FallbackCandidates);
    }

    /// <summary>A missing explicitly preferred transport is a typed validation failure.</summary>
    [Fact]
    public void MissingPreferredTransportFailsInsteadOfSilentlySwitching()
    {
        DeviceProfile profile = new()
        {
            Id = ProfileId.New(),
            UsbIdentity = new UsbSerial("SERIAL_SYNTHETIC"),
            Connection = new ConnectionPreferences { PreferredTransport = TransportPreference.Network },
        };

        Assert.False(ConnectionPlan.TryCreate(profile, ConnectionPolicy.Default, out ConnectionPlan? plan, out ValidationIssue? issue));
        Assert.Null(plan);
        Assert.Equal(CoreErrorCode.InvalidConfiguration, issue?.Code);
    }

    /// <summary>Policy rejects capability mismatch and automatic failback without hysteresis.</summary>
    [Fact]
    public void PolicyRejectsUnsafeRequirements()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConnectionPolicy(
            ConnectionCapabilities.Audio,
            ConnectionCapabilities.Video,
            new RetryPolicy(3, TimeSpan.Zero, TimeSpan.Zero),
            FailbackPolicy.Disabled));

        Assert.Throws<ArgumentOutOfRangeException>(() => new FailbackPolicy(
            true,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1)));
    }
}
