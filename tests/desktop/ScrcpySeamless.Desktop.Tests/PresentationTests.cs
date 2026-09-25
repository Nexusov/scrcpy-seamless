using System.Text.Json;
using ScrcpySeamless.Core.Options;
using ScrcpySeamless.Desktop;
using ScrcpySeamless.Desktop.Presentation;
using Xunit;

namespace ScrcpySeamless.Desktop.Tests;

/// <summary>Protects deterministic preview data and isolated option editing.</summary>
public sealed class PresentationTests
{
    /// <summary>Preview remains detached even when normal composition may use Infrastructure.</summary>
    [Fact]
    public void PreviewCompositionCannotConstructExternalAdapters()
    {
        var normal = DesktopComposition.Create(new DesktopLaunchOptions(false, AppTheme.System, null, false), _ => { });
        Assert.False(normal.IsPreview);
        Assert.Empty(normal.Devices.Cards);
        Assert.Empty(normal.Devices.Scenarios);
        Assert.Empty(normal.Settings.DraftValues);

        var preview = DesktopComposition.Create(new DesktopLaunchOptions(true, AppTheme.System, "fallback", false), _ => { });
        Assert.True(preview.IsPreview);
        Assert.Single(preview.Devices.Cards);
        Assert.Equal("preview-device-7a31", preview.Devices.Cards[0].StableId);
    }

    /// <summary>Normal startup requires one explicit root and preview rejects persistent adapters.</summary>
    [Fact]
    public void LaunchModesCannotSelectAmbiguousOrRelativeStorage()
    {
        DesktopLaunchOptions preview = DesktopLaunchOptions.Parse(["--preview", "--page=settings"]);
        Assert.Equal(DesktopStorageMode.None, preview.StorageMode);
        Assert.Throws<ArgumentException>(() => DesktopLaunchOptions.Parse(["--preview", "--portable"]));
        Assert.Throws<ArgumentException>(() => DesktopLaunchOptions.Parse(["--portable", "--installed"]));
        Assert.Throws<ArgumentException>(() => DesktopLaunchOptions.Parse(
            ["--dev-data-dir=C:\\synthetic\\first", "--dev-data-dir=C:\\synthetic\\second"]));
        Assert.Throws<ArgumentException>(() => DesktopLaunchOptions.Parse(["--preview", "--page=profiles"]));
        Assert.Throws<ArgumentException>(() => DesktopLaunchOptions.Parse(["--dev-data-dir=relative"]));
        Assert.Throws<ArgumentException>(() => DesktopLaunchOptions.Parse(["--ui-scale=not-a-number", "--preview"]));

        DesktopLaunchOptions development = DesktopLaunchOptions.Parse(
            ["--dev-data-dir=C:\\synthetic\\data", "--page=profiles"]);
        Assert.Equal(DesktopStorageMode.Development, development.StorageMode);
        Assert.True(development.ProfilesPage);
    }

    /// <summary>Shows independent transport, process, stream and channel evidence.</summary>
    [Fact]
    public void ScenarioSelectionIsDeterministicAndDoesNotInventReadiness()
    {
        var devices = DesktopComposition.Create(new DesktopLaunchOptions(true, AppTheme.System, null, false), _ => { }).Devices;
        Assert.True(devices.IsEmpty);

        devices.SelectScenario("usb");
        var usb = Assert.Single(devices.Cards);
        Assert.Equal("Available", usb.Availability);
        Assert.Equal("USB selected", usb.SelectedTransport);
        Assert.Equal("Not running", usb.NativeProcess);
        Assert.Equal("Unverified", usb.Video);

        devices.SelectScenario("reconnect");
        var recovering = Assert.Single(devices.Cards);
        Assert.Equal(usb.StableId, recovering.StableId);
        Assert.Equal("Wi-Fi selected", recovering.SelectedTransport);
        Assert.Equal("Running", recovering.NativeProcess);
        Assert.Equal("Recovering", recovering.Streaming);
        Assert.Equal("Observed ready", recovering.Video);
        Assert.Equal("Unverified", recovering.Audio);
        Assert.Equal("Observed ready", recovering.Control);

        devices.SelectScenario("failure");
        Assert.True(Assert.Single(devices.Cards).IsProblem);
        devices.SelectScenario("empty");
        Assert.True(devices.IsEmpty);
    }

    /// <summary>Labels USB selection separately from a prepared Wi-Fi fallback.</summary>
    [Fact]
    public void UsbSelectionRetainsPreparedWirelessFallbackSummary()
    {
        var devices = DesktopComposition.Create(new DesktopLaunchOptions(true, AppTheme.System, "fallback", false), _ => { }).Devices;
        var card = Assert.Single(devices.Cards);

        Assert.True(devices.IsPreview);
        Assert.Equal("Selected transport", card.SessionHeading);
        Assert.Equal("USB selected", card.SelectedTransport);
        Assert.Equal("Wi-Fi fallback prepared", card.FallbackSummary);
        Assert.Equal("Prepared", Assert.Single(card.AvailabilityMetrics, metric => metric.Label == "Wireless fallback").Value);
    }

    /// <summary>Describes Wi-Fi recovery without claiming a second fallback or verified audio.</summary>
    [Fact]
    public void WirelessRecoveryDoesNotClaimAdditionalFallbackOrAudioReadiness()
    {
        var devices = DesktopComposition.Create(new DesktopLaunchOptions(true, AppTheme.System, "reconnect", false), _ => { }).Devices;
        var card = Assert.Single(devices.Cards);

        Assert.True(devices.IsPreview);
        Assert.Equal("Selected transport", card.SessionHeading);
        Assert.Equal("Wi-Fi selected", card.SelectedTransport);
        Assert.Equal("Session recovering", card.SummaryLabel);
        Assert.Equal("Recovery targeting Wi-Fi", card.FallbackSummary);
        Assert.Equal("Running", Assert.Single(card.SessionMetrics, metric => metric.Label == "Native process").Value);
        Assert.Equal("Observed ready", Assert.Single(card.SessionMetrics, metric => metric.Label == "Video").Value);
        Assert.Equal("Unverified", Assert.Single(card.SessionMetrics, metric => metric.Label == "Audio").Value);
        Assert.Equal("Observed ready", Assert.Single(card.SessionMetrics, metric => metric.Label == "Control").Value);
    }

    /// <summary>Filters generated options by localized content without mutating the draft.</summary>
    [Fact]
    public void SettingsSearchAndCategoriesUseGeneratedMetadataAndResources()
    {
        var text = new PresentationText(new Dictionary<string, string>
        {
            ["options.max-size.label"] = "[Expanded maximum picture dimension label]",
        });
        var draft = new InMemoryOptionDraft();
        var settings = new SettingsViewModel(text, draft);
        Assert.Contains(settings.Categories, category => category.Id == "Video");

        settings.SearchText = "Expanded maximum";
        var row = Assert.Single(settings.VisibleRows);
        Assert.Equal("max-size", row.Id);
        Assert.Empty(draft.Values);

        settings.SearchText = string.Empty;
        settings.SelectedCategory = Assert.Single(settings.Categories, category => category.Id == "Audio");
        Assert.All(settings.VisibleRows, option => Assert.Equal("Audio", option.Descriptor.Category));
        Assert.Empty(draft.Values);
    }

    /// <summary>Uses the Core validator and resets only in-memory overrides.</summary>
    [Fact]
    public void DraftValidationAndResetDoNotRewriteStoredSpelling()
    {
        var draft = new InMemoryOptionDraft();
        var settings = new SettingsViewModel(new PresentationText(), draft);
        settings.SearchText = "max-size";
        var row = Assert.Single(settings.VisibleRows);
        row.TextValue = "010";
        Assert.True(row.HasOverride);
        Assert.Null(row.ValidationMessage);
        Assert.Equal("010", draft.Values["max-size"].GetString());

        row.TextValue = "08";
        Assert.NotNull(row.ValidationMessage);
        Assert.Contains(draft.Validate().Diagnostics, diagnostic =>
            diagnostic.OptionId == "max-size" && diagnostic.Code == OptionDiagnosticCode.InvalidValue);

        settings.ResetDraftCommand.Execute(null);
        Assert.Empty(draft.Values);
        Assert.False(row.HasOverride);
        Assert.Null(row.ValidationMessage);
    }

    /// <summary>Preserves explicit false, zero and empty optional values in a detached draft.</summary>
    [Fact]
    public void DraftKeepsDistinctRepresentationsAndPortUnavailable()
    {
        var draft = new InMemoryOptionDraft();
        draft.SetBoolean("always-on-top", false);
        draft.SetText("max-size", "0");
        draft.SetText("new-display", string.Empty);
        Assert.Equal(JsonValueKind.False, draft.Values["always-on-top"].ValueKind);
        Assert.Equal("0", draft.Values["max-size"].GetString());
        Assert.Equal(string.Empty, draft.Values["new-display"].GetString());
        Assert.True(draft.Validate().IsValid);

        var settings = new SettingsViewModel(new PresentationText(), new InMemoryOptionDraft());
        settings.SearchText = "port";
        var port = Assert.Single(settings.VisibleRows, row => row.Id == "port");
        Assert.True(port.CanEdit);
    }

    /// <summary>Rejects preview-only scenarios during normal startup.</summary>
    [Fact]
    public void LaunchArgumentsRequireExplicitPreview()
    {
        Assert.Throws<ArgumentException>(() => DesktopLaunchOptions.Parse(["--scenario=fallback"]));
        Assert.Throws<ArgumentException>(() => DesktopLaunchOptions.Parse(["--search=max-size"]));
        var options = DesktopLaunchOptions.Parse(["--preview", "--scenario=fallback", "--page=settings",
            "--search=audio-output-buffer"]);
        Assert.True(options.Preview);
        Assert.True(options.SettingsPage);
        Assert.Equal("fallback", options.ScenarioId);
        Assert.Equal("audio-output-buffer", options.OptionSearch);
    }
}
