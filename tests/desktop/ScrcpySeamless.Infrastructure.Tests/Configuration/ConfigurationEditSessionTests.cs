using System.Text.Json;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Configuration;
using ScrcpySeamless.Infrastructure.Configuration;
using Xunit;

namespace ScrcpySeamless.Infrastructure.Tests.Configuration;

/// <summary>Exercises the detached editor against the real atomic store in synthetic roots.</summary>
public sealed class ConfigurationEditSessionTests
{
    /// <summary>Opening and applying an untouched empty draft never creates a configuration file.</summary>
    [Fact]
    public async Task MissingConfigurationRemainsInMemoryUntilChangedApply()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "configuration.v2.json");
        ConfigurationEditSession session = new(new VersionedConfigurationStore(path));

        ConfigurationSessionLoadResult loaded = await session.LoadAsync(TestContext.Current.CancellationToken);
        ConfigurationSessionApplyResult noChanges = await session.ApplyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConfigurationSessionLoadStatus.Missing, loaded.Status);
        Assert.Equal(ConfigurationSessionApplyStatus.NoChanges, noChanges.Status);
        Assert.False(File.Exists(path));

        session.SetMirroring(new MirroringPreferences { Reconnect = false });
        ConfigurationSessionApplyResult applied = await session.ApplyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConfigurationSessionApplyStatus.Applied, applied.Status);
        Assert.True(File.Exists(path));
        Assert.False(session.IsDirty);
    }

    /// <summary>Cancel and later Apply use the latest successfully committed baseline.</summary>
    [Fact]
    public async Task ApplyThenEditAndCancelRestoresLastAppliedState()
    {
        using TemporaryDirectory directory = new();
        ConfigurationEditSession session = CreateSession(directory);
        await session.LoadAsync(TestContext.Current.CancellationToken);
        session.SetMirroring(new MirroringPreferences
        {
            Options = new Dictionary<string, JsonElement> { ["video-bit-rate"] = JsonSerializer.SerializeToElement("010") },
        });

        ConfigurationSessionApplyResult applied = await session.ApplyAsync(TestContext.Current.CancellationToken);
        ConfigurationV2 exposed = session.Draft!;
        exposed.Mirroring.Options["video-bit-rate"] = JsonSerializer.SerializeToElement("999");
        session.SetMirroring(new MirroringPreferences { Reconnect = false });
        session.CancelChanges();

        Assert.Equal(ConfigurationSessionApplyStatus.Applied, applied.Status);
        Assert.Equal("010", session.Draft!.Mirroring.Options["video-bit-rate"].GetString());
        Assert.True(session.Draft.Mirroring.Reconnect);
        Assert.False(session.IsDirty);
    }

    /// <summary>A stale byte revision cannot overwrite a peer save; the rejected draft survives.</summary>
    [Fact]
    public async Task ConflictKeepsDirtyDraftAndOriginalRevision()
    {
        using TemporaryDirectory directory = new();
        ConfigurationEditSession first = CreateSession(directory);
        ConfigurationEditSession second = CreateSession(directory);
        await first.LoadAsync(TestContext.Current.CancellationToken);
        await second.LoadAsync(TestContext.Current.CancellationToken);
        first.SetMirroring(new MirroringPreferences { Reconnect = false });
        second.SetMirroring(new MirroringPreferences
        {
            Options = new Dictionary<string, JsonElement> { ["video-bit-rate"] = JsonSerializer.SerializeToElement("010") },
        });

        Assert.Equal(ConfigurationSessionApplyStatus.Applied,
            (await first.ApplyAsync(TestContext.Current.CancellationToken)).Status);
        ConfigurationSessionApplyResult conflict = await second.ApplyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConfigurationSessionApplyStatus.RevisionConflict, conflict.Status);
        Assert.Null(second.Revision);
        Assert.True(second.IsDirty);
        Assert.Equal("010", second.Draft!.Mirroring.Options["video-bit-rate"].GetString());
    }

    /// <summary>Unknown historical options and unrelated profiles survive a scoped mirroring edit.</summary>
    [Fact]
    public async Task MirroringEditPreservesUnknownOptionAndProfileIdentity()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "configuration.v2.json");
        VersionedConfigurationStore store = new(path);
        ProfileId id = ProfileId.New();
        ConfigurationV2 original = new()
        {
            Profiles = [new DeviceProfile { Id = id, UsbIdentity = new UsbSerial("SYNTHETIC_USB"), Alias = "Synthetic" }],
            Mirroring = new MirroringPreferences
            {
                Options = new Dictionary<string, JsonElement>
                {
                    ["future-option"] = JsonSerializer.SerializeToElement("raw-value"),
                    ["audio"] = JsonSerializer.SerializeToElement(false),
                },
            },
        };
        store.Commit(null, original, TestContext.Current.CancellationToken);
        ConfigurationEditSession session = new(store);
        await session.LoadAsync(TestContext.Current.CancellationToken);
        ConfigurationV2 draft = session.Draft!;
        draft.Mirroring.Options["video-bit-rate"] = JsonSerializer.SerializeToElement("010");
        session.ReplaceDraft(draft);
        draft.Mirroring.Options.Clear();

        ConfigurationSessionApplyResult applied = await session.ApplyAsync(TestContext.Current.CancellationToken);
        ConfigurationReadResult reloaded = await store.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConfigurationSessionApplyStatus.Applied, applied.Status);
        Assert.Equal(id, Assert.Single(reloaded.Configuration!.Profiles).Id);
        Assert.Equal("raw-value", reloaded.Configuration.Mirroring.Options["future-option"].GetString());
        Assert.False(reloaded.Configuration.Mirroring.Options["audio"].GetBoolean());
        Assert.Equal("010", reloaded.Configuration.Mirroring.Options["video-bit-rate"].GetString());
    }

    /// <summary>Invalid v2 remains authoritative and is never replaced by an empty editor draft.</summary>
    [Fact]
    public async Task InvalidFileIsReportedWithoutCreatingAnEditableDraft()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "configuration.v2.json");
        await File.WriteAllTextAsync(path, "{broken", TestContext.Current.CancellationToken);
        ConfigurationEditSession session = new(new VersionedConfigurationStore(path));

        ConfigurationSessionLoadResult loaded = await session.LoadAsync(TestContext.Current.CancellationToken);
        ConfigurationSessionApplyResult applied = await session.ApplyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConfigurationSessionLoadStatus.Invalid, loaded.Status);
        Assert.Equal(ConfigurationSessionApplyStatus.NotLoaded, applied.Status);
        Assert.Null(session.Draft);
        Assert.Equal("{broken", File.ReadAllText(path));
    }

    /// <summary>Newly edited invalid recognized options are rejected without losing the draft.</summary>
    [Fact]
    public async Task InvalidEditedOptionDoesNotWriteOrEraseDraft()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "configuration.v2.json");
        ConfigurationEditSession session = new(new VersionedConfigurationStore(path));
        await session.LoadAsync(TestContext.Current.CancellationToken);
        session.SetMirroring(new MirroringPreferences
        {
            Options = new Dictionary<string, JsonElement> { ["video-bit-rate"] = JsonSerializer.SerializeToElement("nonsense") },
        });

        ConfigurationSessionApplyResult result = await session.ApplyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConfigurationSessionApplyStatus.Invalid, result.Status);
        Assert.Contains(result.OptionDiagnostics, diagnostic => diagnostic.OptionId == "video-bit-rate");
        Assert.True(session.IsDirty);
        Assert.False(File.Exists(path));
    }

    /// <summary>Changing only option insertion order does not rotate a valid file or its backup.</summary>
    [Fact]
    public async Task SemanticallyUnchangedApplyLeavesPersistedBytesUntouched()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "configuration.v2.json");
        VersionedConfigurationStore store = new(path);
        ConfigurationV2 original = new()
        {
            Mirroring = new MirroringPreferences
            {
                Options = new Dictionary<string, JsonElement>
                {
                    ["video-bit-rate"] = JsonSerializer.SerializeToElement("010"),
                    ["audio"] = JsonSerializer.SerializeToElement(false),
                },
            },
        };
        store.Commit(null, original, TestContext.Current.CancellationToken);
        byte[] before = File.ReadAllBytes(path);
        ConfigurationEditSession session = new(store);
        await session.LoadAsync(TestContext.Current.CancellationToken);
        session.SetMirroring(new MirroringPreferences
        {
            Options = new Dictionary<string, JsonElement>
            {
                ["audio"] = JsonSerializer.SerializeToElement(false),
                ["video-bit-rate"] = JsonSerializer.SerializeToElement("010"),
            },
        });

        ConfigurationSessionApplyResult result = await session.ApplyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConfigurationSessionApplyStatus.NoChanges, result.Status);
        Assert.False(session.IsDirty);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".bak"));
    }

    /// <summary>A failed reload cannot leave the prior revision authorized to overwrite newer or corrupt bytes.</summary>
    [Fact]
    public async Task InvalidReloadBlocksOldDraftUntilValidStateLoadsAgain()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "configuration.v2.json");
        VersionedConfigurationStore store = new(path);
        store.Commit(null, new ConfigurationV2(), TestContext.Current.CancellationToken);
        ConfigurationEditSession session = new(store);
        Assert.Equal(ConfigurationSessionLoadStatus.Ready,
            (await session.LoadAsync(TestContext.Current.CancellationToken)).Status);
        session.SetReconnect(false);
        await File.WriteAllTextAsync(path, "{broken", TestContext.Current.CancellationToken);

        ConfigurationSessionLoadResult failed = await session.LoadAsync(TestContext.Current.CancellationToken);
        ConfigurationSessionApplyResult blocked = await session.ApplyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConfigurationSessionLoadStatus.Invalid, failed.Status);
        Assert.True(session.RequiresReload);
        Assert.True(session.IsDirty);
        Assert.Equal(ConfigurationSessionApplyStatus.NotLoaded, blocked.Status);
        Assert.Throws<InvalidOperationException>(() => session.SetReconnect(true));
        Assert.Equal("{broken", File.ReadAllText(path));

        await File.WriteAllTextAsync(path, """{"SchemaVersion":2,"Profiles":[],"Mirroring":{"Reconnect":true,"Options":{}}}""",
            TestContext.Current.CancellationToken);
        Assert.Equal(ConfigurationSessionLoadStatus.Ready,
            (await session.LoadAsync(TestContext.Current.CancellationToken)).Status);
        Assert.False(session.RequiresReload);
        Assert.False(session.IsDirty);
    }

    /// <summary>Editing an already invalid recognized value still requires current semantic validity.</summary>
    [Fact]
    public async Task ChangedRecognizedOptionCannotReuseHistoricalDiagnosticAsExemption()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "configuration.v2.json");
        VersionedConfigurationStore store = new(path);
        store.Commit(null, new ConfigurationV2
        {
            Mirroring = new MirroringPreferences
            {
                Options = new Dictionary<string, JsonElement>
                {
                    ["video-bit-rate"] = JsonSerializer.SerializeToElement("old-invalid"),
                },
            },
        }, TestContext.Current.CancellationToken);
        ConfigurationEditSession session = new(store);
        await session.LoadAsync(TestContext.Current.CancellationToken);
        session.SetOption("video-bit-rate", JsonSerializer.SerializeToElement("new-invalid"));

        ConfigurationSessionApplyResult result = await session.ApplyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConfigurationSessionApplyStatus.Invalid, result.Status);
        Assert.Contains(result.OptionDiagnostics, diagnostic => diagnostic.OptionId == "video-bit-rate");
        Assert.Equal("old-invalid", (await store.ReadAsync(TestContext.Current.CancellationToken))
            .Configuration!.Mirroring.Options["video-bit-rate"].GetString());
    }

    /// <summary>An unwritable target reports I/O failure and leaves the detached draft available.</summary>
    [Fact]
    public async Task IoFailureDoesNotAdvanceBaselineOrLoseDraft()
    {
        using TemporaryDirectory directory = new();
        string path = Path.Combine(directory.Path, "configuration.v2.json");
        Directory.CreateDirectory(path);
        ConfigurationEditSession session = new(new VersionedConfigurationStore(path));
        Assert.Equal(ConfigurationSessionLoadStatus.Missing,
            (await session.LoadAsync(TestContext.Current.CancellationToken)).Status);
        session.SetReconnect(false);

        ConfigurationSessionApplyResult result = await session.ApplyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ConfigurationSessionApplyStatus.IoError, result.Status);
        Assert.Equal(nameof(IOException), result.ErrorKind);
        Assert.True(session.IsDirty);
        Assert.Null(session.Revision);
        Assert.False(session.Draft!.Mirroring.Reconnect);
    }

    /// <summary>Constructs a session whose root is owned by this test fixture.</summary>
    private static ConfigurationEditSession CreateSession(TemporaryDirectory directory) =>
        new(new VersionedConfigurationStore(Path.Combine(directory.Path, "configuration.v2.json")));
}
