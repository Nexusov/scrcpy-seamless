using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ScrcpySeamless.Desktop.Presentation;
using ScrcpySeamless.Desktop.Views;
using ScrcpySeamless.Infrastructure.Configuration;
using Xunit;

namespace ScrcpySeamless.Desktop.Tests;

/// <summary>Protects detached profile editing, validation, and deliberate persistence.</summary>
public sealed class ProfilesViewModelTests
{
    /// <summary>Profile fields reflow into one reachable column in a compact window.</summary>
    [AvaloniaFact]
    public void ProfileEditorReflowsAtCompactWidth()
    {
        ProfilesViewModel editor = new(new PresentationText());
        editor.BeginNewProfile();
        ProfilesView view = new() { DataContext = editor };
        Window window = new() { Width = 520, Height = 500, Content = view };

        try
        {
            window.Show();
            window.UpdateLayout();

            Grid fields = Assert.IsType<Grid>(view.FindControl<Grid>("ProfileFields"));
            Control fallback = Assert.IsType<CheckBox>(view.FindControl<CheckBox>("FallbackField"));
            Assert.Single(fields.ColumnDefinitions);
            Assert.Equal(6, Grid.GetRow(fallback));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Invalid endpoint text remains visible and blocks staging until corrected.</summary>
    [Fact]
    public void PreviewEditorRetainsInvalidInputAndStableProfileIdentity()
    {
        ProfilesViewModel editor = new(new PresentationText());
        editor.BeginNewProfile();
        string? profileId = editor.EditingId;

        editor.Alias = "Sample phone";
        editor.ConnectionEndpoint = "invalid-endpoint";

        Assert.True(editor.IsPreview);
        Assert.Equal("invalid-endpoint", editor.ConnectionEndpoint);
        Assert.True(editor.HasValidation);
        Assert.False(editor.CanSaveDraft);
        Assert.False(editor.TrySaveToDraft());
        Assert.Empty(editor.Profiles);

        editor.ConnectionEndpoint = "example.test:5555";
        editor.PairingEndpoint = "example.test:5555";
        Assert.True(editor.HasValidation);
        Assert.False(editor.TrySaveToDraft());

        editor.PairingEndpoint = "example.test:37123";
        editor.SelectedTransport = editor.TransportChoices.Single(choice =>
            choice.Value == ScrcpySeamless.Core.Configuration.TransportPreference.Network);

        Assert.True(editor.TrySaveToDraft());
        Assert.Equal(profileId, Assert.Single(editor.Profiles).Id.ToString());
        Assert.False(editor.CanApply);

        editor.Alias = "Unstaged name";
        editor.CancelEditorChanges();
        Assert.Equal("Sample phone", editor.Alias);
        Assert.Equal(profileId, editor.EditingId);

        editor.ConfirmDeleteCommand.Execute(null);
        Assert.Single(editor.Profiles);
        editor.RequestDeleteCommand.Execute(null);
        Assert.True(editor.DeleteConfirmationVisible);
        editor.ConfirmDeleteCommand.Execute(null);
        Assert.Empty(editor.Profiles);
    }

    /// <summary>Staging a profile leaves disk untouched until revision-aware Apply succeeds.</summary>
    [Fact]
    public async Task SavedProfileNeedsExplicitApplyAndCancelRestoresBaseline()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"seamless-profile-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "configuration.v2.json");

        try
        {
            ConfigurationEditSession session = new(new VersionedConfigurationStore(path));
            Assert.Equal(ConfigurationSessionLoadStatus.Missing,
                (await session.LoadAsync(TestContext.Current.CancellationToken)).Status);
            ProfilesViewModel editor = new(new PresentationText(), session);

            editor.BeginNewProfile();
            editor.Alias = "Offline profile";
            Assert.True(editor.TrySaveToDraft());
            Assert.True(editor.IsDirty);
            Assert.True(editor.CanApply);
            Assert.False(File.Exists(path));

            editor.Cancel();
            Assert.Empty(editor.Profiles);
            Assert.False(session.IsDirty);
            Assert.False(File.Exists(path));

            editor.BeginNewProfile();
            editor.UsbSerial = "SYNTHETIC_SERIAL";
            Assert.True(editor.TrySaveToDraft());
            Assert.Equal(ConfigurationSessionApplyStatus.Applied,
                (await editor.ApplyAsync(TestContext.Current.CancellationToken))?.Status);
            Assert.True(File.Exists(path));
            Assert.False(editor.IsDirty);
            Assert.Single(session.Baseline!.Profiles);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
