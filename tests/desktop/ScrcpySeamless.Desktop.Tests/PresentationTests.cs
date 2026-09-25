using System.Text.Json;
using ScrcpySeamless.Core.Options;
using ScrcpySeamless.Desktop;
using ScrcpySeamless.Desktop.Presentation;
using Xunit;

namespace ScrcpySeamless.Desktop.Tests;

/// <summary>Protects deterministic preview data and isolated option editing.</summary>
public sealed class PresentationTests
{
    /// <summary>Preview is explicit and Desktop has no Infrastructure adapter reference in this slice.</summary>
    [Fact]
    public void PreviewCompositionCannotConstructExternalAdapters()
    {
        var references = typeof(App).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, reference => reference.Name == "ScrcpySeamless.Infrastructure");

        var normal = DesktopComposition.Create(new DesktopLaunchOptions(false, false, null, false), _ => { });
        Assert.False(normal.IsPreview);
        Assert.Empty(normal.Devices.Cards);
        Assert.Empty(normal.Devices.Scenarios);
        Assert.Empty(normal.Settings.DraftValues);

        var preview = DesktopComposition.Create(new DesktopLaunchOptions(true, false, "fallback", false), _ => { });
        Assert.True(preview.IsPreview);
        Assert.Single(preview.Devices.Cards);
        Assert.Equal("preview-device-7a31", preview.Devices.Cards[0].StableId);
    }

    /// <summary>Shows independent transport, process, stream and channel evidence.</summary>
    [Fact]
    public void ScenarioSelectionIsDeterministicAndDoesNotInventReadiness()
    {
        var devices = DesktopComposition.Create(new DesktopLaunchOptions(true, false, null, false), _ => { }).Devices;
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
        Assert.False(port.CanEdit);
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
