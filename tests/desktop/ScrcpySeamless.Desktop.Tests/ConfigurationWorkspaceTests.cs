using System.Text.Json;
using ScrcpySeamless.Core.Configuration;
using ScrcpySeamless.Desktop.Presentation;
using ScrcpySeamless.Infrastructure.Configuration;
using Xunit;

namespace ScrcpySeamless.Desktop.Tests;

/// <summary>Protects the Desktop configuration group's detached edit and migration behavior.</summary>
public sealed class ConfigurationWorkspaceTests
{
    /// <summary>Option edits share the same baseline and Apply followed by Cancel restores the applied value.</summary>
    [Fact]
    public async Task OptionEditorApplyAndCancelUseCommittedBaseline()
    {
        using WorkspaceTestDirectory directory = new();
        string configurationFile = Path.Combine(directory.Path, "configuration.v2.json");
        VersionedConfigurationStore store = new(configurationFile);
        ConfigurationV2 saved = new()
        {
            Mirroring = new MirroringPreferences
            {
                Options = new Dictionary<string, JsonElement>
                {
                    ["video-bit-rate"] = JsonSerializer.SerializeToElement("010"),
                },
            },
        };
        store.Commit(null, saved, TestContext.Current.CancellationToken);
        InMemoryOptionDraft optionDraft = new();
        ConfigurationWorkspaceViewModel workspace = new(new ConfigurationEditSession(store), optionDraft);

        Assert.Equal(ConfigurationSessionLoadStatus.Ready,
            (await workspace.LoadAsync(TestContext.Current.CancellationToken)).Status);
        Assert.Equal("010", optionDraft.Values["video-bit-rate"].GetString());
        optionDraft.SetText("video-bit-rate", "020");
        Assert.True(workspace.IsDirty);
        workspace.CancelChanges();
        Assert.Equal("010", optionDraft.Values["video-bit-rate"].GetString());
        Assert.False(workspace.IsDirty);

        optionDraft.SetText("video-bit-rate", "030");
        Assert.Equal(ConfigurationSessionApplyStatus.Applied,
            (await workspace.ApplyAsync(TestContext.Current.CancellationToken)).Status);
        optionDraft.SetText("video-bit-rate", "040");
        workspace.CancelChanges();

        Assert.Equal("030", optionDraft.Values["video-bit-rate"].GetString());
        Assert.Equal("030", (await store.ReadAsync(TestContext.Current.CancellationToken))
            .Configuration!.Mirroring.Options["video-bit-rate"].GetString());
    }

    /// <summary>Legacy conversion remains a separate reviewed action and reloads v2 after commit.</summary>
    [Fact]
    public async Task MigrationRequiresSeparatePrepareAndConfirmation()
    {
        using WorkspaceTestDirectory directory = new();
        string legacyDirectory = Path.Combine(directory.Path, "synthetic-legacy");
        Directory.CreateDirectory(legacyDirectory);
        await File.WriteAllTextAsync(Path.Combine(legacyDirectory, "phone.json"),
            """{"UsbSerial":"SYNTHETIC_USB","ConnectionMode":"usb"}""",
            TestContext.Current.CancellationToken);
        string configurationFile = Path.Combine(directory.Path, "configuration.v2.json");
        VersionedConfigurationStore store = new(configurationFile);
        ConfigurationWorkspaceViewModel workspace = new(
            new ConfigurationEditSession(store),
            new InMemoryOptionDraft(),
            new LegacyMigrationCoordinator(legacyDirectory, store));
        await workspace.LoadAsync(TestContext.Current.CancellationToken);

        ConfigurationWorkspaceMigrationResult prepared = await workspace.PrepareMigrationAsync(TestContext.Current.CancellationToken);
        Assert.Equal(LegacyMigrationStatus.Ready, prepared.Status);
        Assert.True(workspace.HasPreparedMigration);
        Assert.Equal(1, workspace.MigrationSummary!.ProfileCount);
        Assert.False(File.Exists(configurationFile));
        workspace.CancelMigration();
        Assert.False(workspace.HasPreparedMigration);
        Assert.False(File.Exists(configurationFile));

        await workspace.PrepareMigrationAsync(TestContext.Current.CancellationToken);
        ConfigurationWorkspaceMigrationResult committed = await workspace.CommitMigrationAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LegacyMigrationStatus.Migrated, committed.Status);
        Assert.Equal(ConfigurationSessionLoadStatus.Ready, committed.ReloadResult!.Status);
        Assert.Equal(ConfigurationWorkspaceStatus.Ready, workspace.Status);
        Assert.Single(workspace.Draft!.Profiles);
        Assert.True(File.Exists(configurationFile));
    }

    /// <summary>An editor event before loading cannot leak into a future persistent draft.</summary>
    [Fact]
    public async Task EditBeforeLoadIsRejectedAndEditorRestored()
    {
        using WorkspaceTestDirectory directory = new();
        VersionedConfigurationStore store = new(Path.Combine(directory.Path, "configuration.v2.json"));
        InMemoryOptionDraft optionDraft = new();
        ConfigurationWorkspaceViewModel workspace = new(new ConfigurationEditSession(store), optionDraft);

        optionDraft.SetText("video-bit-rate", "010");

        Assert.Empty(optionDraft.Values);
        Assert.Equal("EditUnavailable", workspace.LastCommandErrorKind);
        Assert.False(workspace.IsDirty);
        Assert.Equal(ConfigurationSessionLoadStatus.Missing,
            (await workspace.LoadAsync(TestContext.Current.CancellationToken)).Status);
        Assert.Empty(workspace.Draft!.Mirroring.Options);
    }
}

/// <summary>Removes only the exact disposable synthetic directory created by a test.</summary>
internal sealed class WorkspaceTestDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
        "scrcpy-workspace-test-" + Guid.NewGuid().ToString("N"));

    public WorkspaceTestDirectory() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        string absolute = System.IO.Path.GetFullPath(Path);
        string temporaryRoot = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());

        if (!absolute.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to remove data outside the temporary test root.");
        }

        Directory.Delete(absolute, recursive: true);
    }
}
