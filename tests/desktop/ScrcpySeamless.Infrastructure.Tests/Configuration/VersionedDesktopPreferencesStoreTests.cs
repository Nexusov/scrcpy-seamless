using ScrcpySeamless.Core.Configuration;
using ScrcpySeamless.Infrastructure.Configuration;
using Xunit;

namespace ScrcpySeamless.Infrastructure.Tests.Configuration;

/// <summary>Exercises the separate Desktop preferences document through real temporary files.</summary>
public sealed class VersionedDesktopPreferencesStoreTests
{
    /// <summary>Missing preferences use safe defaults without creating a directory or document.</summary>
    [Fact]
    public async Task MissingPreferencesRemainInMemoryOnly()
    {
        using TemporaryDirectory directory = new();
        string root = Path.Combine(directory.Path, "selected-root");
        string path = Path.Combine(root, "desktop-preferences.json");
        VersionedDesktopPreferencesStore store = new(path);

        DesktopPreferencesReadResult read = await store.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(DesktopPreferencesReadStatus.Missing, read.Status);
        Assert.Null(read.Revision);
        Assert.Equal(DesktopTheme.System, read.Preferences!.Appearance.Theme);
        Assert.Equal(100, read.Preferences.Appearance.ScalePercent);
        Assert.Equal("Control+F", read.Preferences.Shortcuts.EffectiveBinding(DesktopCommandIds.FocusSettingsSearch));
        Assert.False(Directory.Exists(root));
    }

    /// <summary>Successful save survives restart while the old revision remains in its backup.</summary>
    [Fact]
    public async Task CommitPersistsTypedPreferencesAndBacksUpPreviousRevision()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "desktop-preferences.json");
        VersionedDesktopPreferencesStore firstProcess = new(path);
        DesktopPreferences first = new();
        DesktopPreferencesCommitResult created = firstProcess.Commit(null, first, TestContext.Current.CancellationToken);
        DesktopPreferencesReadResult baseline = await firstProcess.ReadAsync(TestContext.Current.CancellationToken);
        byte[] originalBytes = File.ReadAllBytes(path);
        DesktopPreferences second = CustomPreferences();
        DesktopPreferencesCommitResult updated = firstProcess.Commit(baseline.Revision, second, TestContext.Current.CancellationToken);
        VersionedDesktopPreferencesStore restartedProcess = new(path);
        DesktopPreferencesReadResult restored = await restartedProcess.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(DesktopPreferencesCommitStatus.Committed, created.Status);
        Assert.Equal(DesktopPreferencesCommitStatus.Committed, updated.Status);
        Assert.Equal(DesktopPreferencesReadStatus.Loaded, restored.Status);
        Assert.Equal(DesktopTheme.Dark, restored.Preferences!.Appearance.Theme);
        Assert.Equal("#1166AA", restored.Preferences.Appearance.AccentColor);
        Assert.Equal("Synthetic Sans", restored.Preferences.Appearance.FontFamily);
        Assert.Equal(125, restored.Preferences.Appearance.ScalePercent);
        Assert.Equal(string.Empty, restored.Preferences.Shortcuts.EffectiveBinding(DesktopCommandIds.ShowDevices));
        Assert.Equal("Control+3", restored.Preferences.Shortcuts.EffectiveBinding(DesktopCommandIds.ShowSettings));
        Assert.Equal(originalBytes, File.ReadAllBytes(path + ".bak"));
    }

    /// <summary>Equivalent Apply neither rewrites the document nor rotates its backup.</summary>
    [Fact]
    public async Task UnchangedCommitPreservesExactBytesAndRevision()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "desktop-preferences.json");
        VersionedDesktopPreferencesStore store = new(path);
        DesktopPreferencesCommitResult created = store.Commit(null, CustomPreferences(), TestContext.Current.CancellationToken);
        DesktopPreferencesReadResult loaded = await store.ReadAsync(TestContext.Current.CancellationToken);
        byte[] before = File.ReadAllBytes(path);

        DesktopPreferencesCommitResult unchanged = store.Commit(loaded.Revision, loaded.Preferences!, TestContext.Current.CancellationToken);

        Assert.Equal(DesktopPreferencesCommitStatus.Unchanged, unchanged.Status);
        Assert.Equal(created.Revision, unchanged.Revision);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".bak"));
    }

    /// <summary>A stale second writer cannot erase the first writer's newer preferences.</summary>
    [Fact]
    public async Task StaleWriterReportsConflictWithoutLostUpdate()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "desktop-preferences.json");
        VersionedDesktopPreferencesStore firstWriter = new(path);
        VersionedDesktopPreferencesStore secondWriter = new(path);
        DesktopPreferencesReadResult firstBaseline = await firstWriter.ReadAsync(TestContext.Current.CancellationToken);
        DesktopPreferencesReadResult secondBaseline = await secondWriter.ReadAsync(TestContext.Current.CancellationToken);

        DesktopPreferencesCommitResult first = firstWriter.Commit(firstBaseline.Revision, CustomPreferences(), TestContext.Current.CancellationToken);
        DesktopPreferencesCommitResult stale = secondWriter.Commit(secondBaseline.Revision, new DesktopPreferences(), TestContext.Current.CancellationToken);
        DesktopPreferencesReadResult current = await secondWriter.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(DesktopPreferencesCommitStatus.Committed, first.Status);
        Assert.Equal(DesktopPreferencesCommitStatus.RevisionConflict, stale.Status);
        Assert.Equal(first.Revision, current.Revision);
        Assert.Equal(DesktopTheme.Dark, current.Preferences!.Appearance.Theme);
    }

    /// <summary>Invalid and future-version documents remain byte-for-byte untouched.</summary>
    [Theory]
    [InlineData("{broken", DesktopPreferencesReadStatus.Invalid)]
    [InlineData("{\"SchemaVersion\":99,\"Appearance\":{\"Theme\":\"System\",\"AccentColor\":null,\"FontFamily\":null,\"ScalePercent\":100},\"Shortcuts\":{\"Bindings\":{}}}", DesktopPreferencesReadStatus.Unsupported)]
    [InlineData("{\"SchemaVersion\":99,\"FutureField\":true}", DesktopPreferencesReadStatus.Unsupported)]
    public async Task InvalidOrUnsupportedFileCannotBeSilentlyReplaced(string contents, DesktopPreferencesReadStatus status)
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "desktop-preferences.json");
        await File.WriteAllTextAsync(path, contents, TestContext.Current.CancellationToken);
        VersionedDesktopPreferencesStore store = new(path);

        DesktopPreferencesReadResult read = await store.ReadAsync(TestContext.Current.CancellationToken);
        DesktopPreferencesCommitResult refused = store.Commit(read.Revision, new DesktopPreferences(), TestContext.Current.CancellationToken);

        Assert.Equal(status, read.Status);
        Assert.Equal(DesktopPreferencesCommitStatus.InvalidExistingDocument, refused.Status);
        Assert.Equal(contents, File.ReadAllText(path));
        Assert.False(File.Exists(path + ".bak"));
    }

    /// <summary>Unsafe edits fail validation before touching a valid saved document.</summary>
    [Fact]
    public void ShortcutCollisionAndReservedBindingDoNotReplaceSavedPreferences()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "desktop-preferences.json");
        VersionedDesktopPreferencesStore store = new(path);
        DesktopPreferencesCommitResult baseline = store.Commit(null, new DesktopPreferences(), TestContext.Current.CancellationToken);
        byte[] before = File.ReadAllBytes(path);
        DesktopPreferences collision = new()
        {
            Shortcuts = new DesktopShortcutPreferences
            {
                Bindings = new Dictionary<string, string>
                {
                    [DesktopCommandIds.FocusSettingsSearch] = "Control+1",
                    [DesktopCommandIds.ShowDevices] = "Control+1",
                },
            },
        };
        DesktopPreferences reserved = new()
        {
            Shortcuts = new DesktopShortcutPreferences
            {
                Bindings = new Dictionary<string, string>
                {
                    [DesktopCommandIds.FocusSettingsSearch] = "Control+C",
                },
            },
        };

        DesktopPreferencesCommitResult collisionResult = store.Commit(baseline.Revision, collision, TestContext.Current.CancellationToken);
        DesktopPreferencesCommitResult reservedResult = store.Commit(baseline.Revision, reserved, TestContext.Current.CancellationToken);

        Assert.Equal(DesktopPreferencesCommitStatus.InvalidPreferences, collisionResult.Status);
        Assert.Equal(DesktopPreferencesCommitStatus.InvalidPreferences, reservedResult.Status);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    /// <summary>A missing binding inherits its command default while explicit empty disables it.</summary>
    [Fact]
    public void MissingAndEmptyShortcutBindingsRemainDistinct()
    {
        DesktopShortcutPreferences shortcuts = new()
        {
            Bindings = new Dictionary<string, string>
            {
                [DesktopCommandIds.ShowDevices] = string.Empty,
            },
        };

        Assert.Equal("Control+F", shortcuts.EffectiveBinding(DesktopCommandIds.FocusSettingsSearch));
        Assert.Equal(string.Empty, shortcuts.EffectiveBinding(DesktopCommandIds.ShowDevices));
        Assert.Equal("Control+2", shortcuts.EffectiveBinding(DesktopCommandIds.ShowSettings));
        Assert.Empty(shortcuts.Validate(nameof(DesktopPreferences.Shortcuts)));

        DesktopShortcutPreferences inheritedCollision = new()
        {
            Bindings = new Dictionary<string, string>
            {
                [DesktopCommandIds.ShowDevices] = "Control+F",
            },
        };
        Assert.NotEmpty(inheritedCollision.Validate(nameof(DesktopPreferences.Shortcuts)));
    }

    /// <summary>A failed replacement leaves the prior complete document and revision available.</summary>
    [Fact]
    public async Task FailedReplacementPreservesExistingPreferences()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "desktop-preferences.json");
        VersionedDesktopPreferencesStore store = new(path);
        store.Commit(null, new DesktopPreferences(), TestContext.Current.CancellationToken);
        DesktopPreferencesReadResult baseline = await store.ReadAsync(TestContext.Current.CancellationToken);
        byte[] before = File.ReadAllBytes(path);
        Directory.CreateDirectory(path + ".bak");

        Exception error = Record.Exception(() => store.Commit(
            baseline.Revision, CustomPreferences(), TestContext.Current.CancellationToken))!;
        DesktopPreferencesReadResult after = await store.ReadAsync(TestContext.Current.CancellationToken);

        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(baseline.Revision, after.Revision);
        Assert.Equal(DesktopPreferencesReadStatus.Loaded, after.Status);
    }

    /// <summary>Creates a synthetic valid request with a disabled shortcut.</summary>
    private static DesktopPreferences CustomPreferences()
    {
        return new DesktopPreferences
        {
            Appearance = new DesktopAppearancePreferences
            {
                Theme = DesktopTheme.Dark,
                AccentColor = "#1166AA",
                FontFamily = "Synthetic Sans",
                ScalePercent = 125,
            },
            Shortcuts = new DesktopShortcutPreferences
            {
                Bindings = new Dictionary<string, string>
                {
                    [DesktopCommandIds.FocusSettingsSearch] = "Control+F",
                    [DesktopCommandIds.ShowDevices] = string.Empty,
                    [DesktopCommandIds.ShowSettings] = "Control+3",
                },
            },
        };
    }
}
