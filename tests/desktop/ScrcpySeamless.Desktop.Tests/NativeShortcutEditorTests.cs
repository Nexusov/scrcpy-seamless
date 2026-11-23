using System.Text.Json;
using ScrcpySeamless.Core.Configuration;
using ScrcpySeamless.Core.Options;
using ScrcpySeamless.Desktop.Presentation;
using ScrcpySeamless.Infrastructure.Configuration;
using Xunit;

namespace ScrcpySeamless.Desktop.Tests;

public sealed class NativeShortcutEditorTests
{
    [Fact]
    public void EditorChangesOnlyDetachedOptionAndRetainsDefaultAliases()
    {
        InMemoryOptionDraft draft = new(new Dictionary<string, JsonElement>
        {
            ["video-buffer"] = JsonSerializer.SerializeToElement("0250"),
        });
        SettingsViewModel settings = new(new PresentationText(), draft);
        Assert.Equal(34, settings.ShortcutEditor.Rows.Count);
        Assert.DoesNotContain(settings.VisibleRows, row => row.Id == NativeShortcutBindings.OptionId);
        NativeShortcutEditorRow home = settings.ShortcutEditor.Rows.Single(row => row.Id == "home");
        home.Gesture = "F12";
        home.Mode = home.Modes[1];
        Assert.True(draft.Validate().IsValid);
        Assert.Equal("0250", draft.Values["video-buffer"].GetString());
        Assert.Equal("{\"home\":\"F12\"}", draft.Values[NativeShortcutBindings.OptionId].GetString());
        home.Mode = home.Modes[2];
        Assert.Equal("{\"home\":\"\"}", draft.Values[NativeShortcutBindings.OptionId].GetString());
        home.Mode = home.Modes[0];
        Assert.False(draft.TryGet(NativeShortcutBindings.OptionId, out _));
        Assert.Contains("MOD+Backspace", settings.ShortcutEditor.Rows.Single(row => row.Id == "back").DefaultGesture);
    }

    [Fact]
    public void InvalidAndConflictingEditsBlockCoreValidationWithoutAffectingOtherValues()
    {
        InMemoryOptionDraft draft = new();
        NativeShortcutEditor editor = new(draft, new PresentationText(), () => { });
        NativeShortcutEditorRow home = editor.Rows.Single(row => row.Id == "home");
        home.Mode = home.Modes[1];
        Assert.True(editor.HasValidation);
        Assert.False(draft.Validate().IsValid);
        home.Gesture = "MOD+B";
        Assert.True(editor.HasValidation);
        Assert.False(draft.Validate().IsValid);
        home.Gesture = "F12";
        Assert.False(editor.HasValidation);
        Assert.True(draft.Validate().IsValid);
    }

    [Fact]
    public void MalformedStoredValueRequiresExplicitReset()
    {
        const string invalid = "{\"futureAction\":\"F12\"}";
        InMemoryOptionDraft draft = new(new Dictionary<string, JsonElement>
        {
            [NativeShortcutBindings.OptionId] = JsonSerializer.SerializeToElement(invalid),
        });
        NativeShortcutEditor editor = new(draft, new PresentationText(), () => { });
        Assert.False(editor.CanEdit);
        Assert.True(editor.HasValidation);
        Assert.Equal(invalid, draft.Values[NativeShortcutBindings.OptionId].GetString());
        editor.ResetCommand.Execute(null);
        Assert.True(editor.CanEdit);
        Assert.False(editor.HasValidation);
        Assert.False(draft.TryGet(NativeShortcutBindings.OptionId, out _));
    }

    [Fact]
    public async Task ApplyAndCancelUseTheExistingConfigurationBaseline()
    {
        using WorkspaceTestDirectory directory = new();
        VersionedConfigurationStore store = new(Path.Combine(directory.Path, "configuration.v2.json"));
        store.Commit(null, new ConfigurationV2(), TestContext.Current.CancellationToken);
        InMemoryOptionDraft draft = new();
        ConfigurationWorkspaceViewModel workspace = new(new ConfigurationEditSession(store), draft);
        await workspace.LoadAsync(TestContext.Current.CancellationToken);
        SettingsViewModel settings = new(new PresentationText(), draft, false);
        workspace.DraftReplaced += () => settings.RefreshValues(draft.Values);
        NativeShortcutEditorRow home = settings.ShortcutEditor.Rows.Single(row => row.Id == "home");
        home.Gesture = "F12";
        home.Mode = home.Modes[1];
        Assert.True(workspace.IsDirty);
        await workspace.ApplyAsync(TestContext.Current.CancellationToken);
        Assert.False(workspace.IsDirty);
        home.Gesture = "F10";
        workspace.CancelChanges();
        Assert.Equal("F12", home.Gesture);
        Assert.True(home.IsCustom);
        Assert.Equal("{\"home\":\"F12\"}", draft.Values[NativeShortcutBindings.OptionId].GetString());
    }
}
