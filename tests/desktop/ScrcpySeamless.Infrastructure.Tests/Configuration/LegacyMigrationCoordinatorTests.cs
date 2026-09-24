using ScrcpySeamless.Infrastructure.Configuration;
using Xunit;

namespace ScrcpySeamless.Infrastructure.Tests.Configuration;

/// <summary>Protects validation-first migration and unchanged legacy files.</summary>
public sealed class LegacyMigrationCoordinatorTests
{
    /// <summary>A successful migration leaves both v1 sources intact and becomes idempotent.</summary>
    [Fact]
    public async Task MigrationPreservesLegacyInputsAndRepeatedRunUsesV2()
    {
        using TemporaryDirectory directory = new();
        string phonePath = Path.Combine(directory.Path, "phone.json");
        string settingsPath = Path.Combine(directory.Path, "scrcpy-settings.json");
        string phone = Fixture("legacy-phone-mdns.json");
        string settings = Fixture("legacy-settings.json");
        await File.WriteAllTextAsync(phonePath, phone, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(settingsPath, settings, TestContext.Current.CancellationToken);
        string v2Path = Path.Combine(directory.Path, "data", "configuration.v2.json");
        LegacyMigrationCoordinator coordinator = new(directory.Path, new VersionedConfigurationStore(v2Path));

        LegacyMigrationOperation first = await coordinator.MigrateAsync(TestContext.Current.CancellationToken);
        LegacyMigrationOperation repeated = await coordinator.MigrateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LegacyMigrationStatus.Migrated, first.Status);
        Assert.Equal(LegacyMigrationStatus.AlreadyV2, repeated.Status);
        Assert.Equal(phone, File.ReadAllText(phonePath));
        Assert.Equal(settings, File.ReadAllText(settingsPath));
        Assert.True(File.Exists(v2Path));
    }

    /// <summary>Changes between preparation and commit prevent stale data from being saved.</summary>
    [Fact]
    public async Task ChangedLegacyInputRejectsPreparedMigration()
    {
        using TemporaryDirectory directory = new();
        string phonePath = Path.Combine(directory.Path, "phone.json");
        await File.WriteAllTextAsync(phonePath, Fixture("legacy-phone-usb.json"), TestContext.Current.CancellationToken);
        string v2Path = Path.Combine(directory.Path, "data", "configuration.v2.json");
        LegacyMigrationCoordinator coordinator = new(directory.Path, new VersionedConfigurationStore(v2Path));
        LegacyMigrationOperation prepared = await coordinator.PrepareAsync(TestContext.Current.CancellationToken);
        Assert.Equal(LegacyMigrationStatus.Ready, prepared.Status);

        await File.WriteAllTextAsync(phonePath, Fixture("legacy-phone-address.json"), TestContext.Current.CancellationToken);
        LegacyMigrationOperation result = coordinator.CommitPrepared(prepared.Proposal!, TestContext.Current.CancellationToken);

        Assert.Equal(LegacyMigrationStatus.LegacyChanged, result.Status);
        Assert.False(File.Exists(v2Path));
    }

    /// <summary>Commit recomputes the validated state and ignores mutations of a preview object.</summary>
    [Fact]
    public async Task PreparedPreviewMutationCannotChangeCommittedConfiguration()
    {
        using TemporaryDirectory directory = new();
        await File.WriteAllTextAsync(
            Path.Combine(directory.Path, "scrcpy-settings.json"),
            Fixture("legacy-settings.json"),
            TestContext.Current.CancellationToken);
        string v2Path = Path.Combine(directory.Path, "data", "configuration.v2.json");
        VersionedConfigurationStore store = new(v2Path);
        LegacyMigrationCoordinator coordinator = new(directory.Path, store);
        LegacyMigrationOperation prepared = await coordinator.PrepareAsync(TestContext.Current.CancellationToken);
        Assert.Equal(LegacyMigrationStatus.Ready, prepared.Status);

        prepared.Proposal!.Configuration.Mirroring.Options.Clear();
        LegacyMigrationOperation committed = coordinator.CommitPrepared(prepared.Proposal, TestContext.Current.CancellationToken);
        ConfigurationReadResult stored = await store.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LegacyMigrationStatus.Migrated, committed.Status);
        Assert.True(stored.Configuration!.Mirroring.Options.ContainsKey("max-fps"));
    }

    /// <summary>An orphaned temporary file has no authority over the next complete migration.</summary>
    [Fact]
    public async Task InterruptedTemporaryWriteCanBeRetried()
    {
        using TemporaryDirectory directory = new();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "phone.json"), Fixture("legacy-phone-usb.json"), TestContext.Current.CancellationToken);
        string v2Path = Path.Combine(directory.Path, "data", "configuration.v2.json");
        Directory.CreateDirectory(Path.GetDirectoryName(v2Path)!);
        await File.WriteAllTextAsync(v2Path + ".abandoned.tmp", "{partial", TestContext.Current.CancellationToken);
        LegacyMigrationCoordinator coordinator = new(directory.Path, new VersionedConfigurationStore(v2Path));

        LegacyMigrationOperation result = await coordinator.MigrateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LegacyMigrationStatus.Migrated, result.Status);
        Assert.True(File.Exists(v2Path));
        Assert.Equal("{partial", File.ReadAllText(v2Path + ".abandoned.tmp"));
    }

    /// <summary>Pre-cancelled migration does not read or write user files.</summary>
    [Fact]
    public async Task CancelledPreparationLeavesNoV2State()
    {
        using TemporaryDirectory directory = new();
        string v2Path = Path.Combine(directory.Path, "data", "configuration.v2.json");
        LegacyMigrationCoordinator coordinator = new(directory.Path, new VersionedConfigurationStore(v2Path));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.PrepareAsync(cancellation.Token));
        Assert.False(File.Exists(v2Path));
    }

    /// <summary>Loads only sanitized test fixtures.</summary>
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
