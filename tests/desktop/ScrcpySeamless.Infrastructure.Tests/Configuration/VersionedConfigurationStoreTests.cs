using System.Text.Json;
using ScrcpySeamless.Core.Configuration;
using ScrcpySeamless.Infrastructure.Configuration;
using Xunit;

namespace ScrcpySeamless.Infrastructure.Tests.Configuration;

/// <summary>Tests the real atomic filesystem boundary only inside isolated temp directories.</summary>
public sealed class VersionedConfigurationStoreTests
{
    /// <summary>Portable and installed locations derive from explicit roots.</summary>
    [Fact]
    public void ApplicationDataModesHaveIndependentRoots()
    {
        string applicationRoot = Path.Combine(Path.GetTempPath(), "synthetic-app");
        string userRoot = Path.Combine(Path.GetTempPath(), "synthetic-user");

        ApplicationDataPaths portable = ApplicationDataPaths.Resolve(ApplicationDataMode.Portable, applicationRoot);
        ApplicationDataPaths installed = ApplicationDataPaths.Resolve(ApplicationDataMode.Installed, applicationRoot, userRoot);

        Assert.Equal(Path.Combine(applicationRoot, "data", "configuration.v2.json"), portable.ConfigurationFile);
        Assert.Equal(Path.Combine(userRoot, "scrcpy-seamless", "configuration.v2.json"), installed.ConfigurationFile);
        Assert.Throws<ArgumentNullException>(() => ApplicationDataPaths.Resolve(ApplicationDataMode.Installed, applicationRoot));
    }

    /// <summary>Only the exact snapshot revision may be replaced and the old bytes remain backed up.</summary>
    [Fact]
    public async Task CommitUsesByteRevisionAndCreatesRecoverableBackup()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "configuration.v2.json");
        VersionedConfigurationStore store = new(path);
        ConfigurationV2 first = MigrateFixture();
        ConfigurationCommitResult created = store.Commit(null, first, TestContext.Current.CancellationToken);
        ConfigurationReadResult snapshot = await store.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConfigurationCommitStatus.Committed, created.Status);
        Assert.Equal(ConfigurationReadStatus.Loaded, snapshot.Status);
        Assert.Equal(created.Revision, snapshot.Revision);
        Assert.Equal("TEST_DEVICE_01", Assert.Single(snapshot.Configuration!.Profiles).UsbIdentity?.Value);

        ConfigurationV2 second = new()
        {
            Profiles = first.Profiles,
            Mirroring = new MirroringPreferences { Reconnect = false, Options = first.Mirroring.Options },
        };
        ConfigurationCommitResult stale = store.Commit(null, second, TestContext.Current.CancellationToken);
        ConfigurationCommitResult updated = store.Commit(snapshot.Revision, second, TestContext.Current.CancellationToken);
        ConfigurationReadResult after = await store.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConfigurationCommitStatus.RevisionConflict, stale.Status);
        Assert.Equal(ConfigurationCommitStatus.Committed, updated.Status);
        Assert.False(after.Configuration!.Mirroring.Reconnect);
        Assert.True(File.Exists(path + ".bak"));
        Assert.Equal(JsonDocument.Parse(File.ReadAllText(path + ".bak")).RootElement.GetProperty("Mirroring").GetProperty("Reconnect").GetBoolean(), first.Mirroring.Reconnect);
    }

    /// <summary>Corrupt v2 state is reported and never silently replaced by a new migration.</summary>
    [Fact]
    public async Task CorruptV2IsNotTreatedAsMissing()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "configuration.v2.json");
        await File.WriteAllTextAsync(path, "{broken", TestContext.Current.CancellationToken);
        VersionedConfigurationStore store = new(path);

        ConfigurationReadResult snapshot = await store.ReadAsync(TestContext.Current.CancellationToken);
        LegacyMigrationCoordinator migration = new(directory.Path, store);
        LegacyMigrationOperation outcome = await migration.MigrateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConfigurationReadStatus.Invalid, snapshot.Status);
        Assert.Equal(LegacyMigrationStatus.InvalidV2, outcome.Status);
        Assert.Equal("{broken", File.ReadAllText(path));
    }

    /// <summary>Cancellation before commit leaves the previous document untouched.</summary>
    [Fact]
    public void CancelledCommitDoesNotCreateConfiguration()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "configuration.v2.json");
        VersionedConfigurationStore store = new(path);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => store.Commit(null, MigrateFixture(), cancellation.Token));
        Assert.False(File.Exists(path));
    }

    /// <summary>Creates known v2 state from a sanitized static legacy sample.</summary>
    private static ConfigurationV2 MigrateFixture()
    {
        string phone = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "legacy-phone-usb.json"));
        LegacyMigrationResult migrated = LegacyConfigurationMigrator.Migrate(phone, null);
        Assert.True(migrated.Succeeded);
        return migrated.Configuration!;
    }
}

/// <summary>Owns a random temporary test root and removes only that exact root.</summary>
internal sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "scrcpy-phase3-test-" + Guid.NewGuid().ToString("N"));

    public TemporaryDirectory() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        string fullPath = System.IO.Path.GetFullPath(Path);
        string temporaryRoot = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());

        if (!fullPath.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to remove a directory outside the test temp root.");
        }

        Directory.Delete(fullPath, recursive: true);
    }
}
