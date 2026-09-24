using System.Text.Json;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Application.Connection;
using ScrcpySeamless.Core.Application.NativeHost;
using ScrcpySeamless.Core.Configuration;
using Xunit;

namespace ScrcpySeamless.Core.Tests;

/// <summary>Protects the typed native launch snapshot without starting a process.</summary>
public sealed class NativeHostContractTests
{
    /// <summary>Launch input remains stable after caller preferences change or JSON is disposed.</summary>
    [Fact]
    public void StartRequestCapturesMirroringOptions()
    {
        DeviceProfile profile = new()
        {
            Id = ProfileId.New(),
            UsbIdentity = new UsbSerial("SERIAL_SYNTHETIC"),
        };

        Assert.True(ConnectionPlan.TryCreate(profile, ConnectionPolicy.Default, out ConnectionPlan? plan, out _));
        Assert.NotNull(plan);

        NativeStartRequest request;

        using (JsonDocument document = JsonDocument.Parse("\"synthetic\""))
        {
            MirroringPreferences preferences = new()
            {
                Reconnect = true,
                Options = new Dictionary<string, JsonElement> { ["video-codec"] = document.RootElement },
            };
            request = new NativeStartRequest(SessionId.New(), plan, preferences);
            preferences.Options.Clear();
        }

        Assert.Equal(profile.Id, request.ProfileId);
        Assert.Equal("synthetic", request.Mirroring.Options["video-codec"].GetString());

        request.Mirroring.Options.Clear();
        Assert.Single(request.Mirroring.Options);
    }

    /// <summary>Default struct identity cannot authorize a native launch.</summary>
    [Fact]
    public void StartRequestRequiresSessionIdentity()
    {
        DeviceProfile profile = new()
        {
            Id = ProfileId.New(),
            UsbIdentity = new UsbSerial("SERIAL_SYNTHETIC"),
        };

        Assert.True(ConnectionPlan.TryCreate(profile, ConnectionPolicy.Default, out ConnectionPlan? plan, out _));
        Assert.NotNull(plan);

        Assert.Throws<ArgumentException>(() => new NativeStartRequest(default, plan, new MirroringPreferences()));
    }
}
