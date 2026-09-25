using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Configuration;
using ScrcpySeamless.Infrastructure.Configuration;

namespace ScrcpySeamless.Desktop.Presentation;

/// <summary>One selectable saved profile without implying current device availability.</summary>
public sealed record SavedProfileChoice(ProfileId Id, string Label);

/// <summary>A localized transport preference choice for saved profile policy.</summary>
public sealed record ProfileTransportChoice(TransportPreference Value, string Label);

/// <summary>Edits saved profiles in a detached v2 draft or isolated preview memory.</summary>
public sealed class ProfilesViewModel : ObservableViewModel
{
    private readonly PresentationText text;
    private readonly ConfigurationEditSession? session;
    private ConfigurationWorkspaceViewModel? workspace;
    private readonly IReadOnlyList<DeviceProfile> previewBaseline;
    private readonly List<DeviceProfile> previewDraft;
    private SavedProfileChoice? selectedProfile;
    private DeviceProfile? selectedBaseline;
    private ProfileId? editingId;
    private DeviceId? editingDeviceId;
    private string alias = string.Empty;
    private string usbSerial = string.Empty;
    private string mdnsService = string.Empty;
    private string pairingEndpoint = string.Empty;
    private string connectionEndpoint = string.Empty;
    private ProfileTransportChoice selectedTransport;
    private bool allowFallback = true;
    private bool deleteConfirmationVisible;
    private string? validationMessage;
    private string? statusMessage;
    private bool isApplying;

    /// <summary>Creates an editor; without a session it operates only on isolated preview memory.</summary>
    public ProfilesViewModel(
        PresentationText text,
        ConfigurationEditSession? session = null,
        IReadOnlyList<DeviceProfile>? previewProfiles = null)
    {
        this.text = text;
        this.session = session;
        previewBaseline = previewProfiles?.ToArray() ?? [];
        previewDraft = [.. previewBaseline];
        TransportChoices =
        [
            new(TransportPreference.Automatic, text.Get("profiles.transport.automatic")),
            new(TransportPreference.Usb, text.Get("profiles.transport.usb")),
            new(TransportPreference.Network, text.Get("profiles.transport.network")),
        ];
        selectedTransport = TransportChoices[0];
        Profiles = [];
        Eyebrow = text.Get("profiles.eyebrow");
        Title = text.Get("profiles.title");
        Subtitle = text.Get("profiles.subtitle");
        ListLabel = text.Get("profiles.list");
        EmptyLabel = text.Get("profiles.empty");
        AddLabel = text.Get("profiles.add");
        AliasLabel = text.Get("profiles.alias");
        UsbLabel = text.Get("profiles.usb");
        MdnsLabel = text.Get("profiles.mdns");
        PairingLabel = text.Get("profiles.pairing");
        ConnectionLabel = text.Get("profiles.connection");
        TransportLabel = text.Get("profiles.transport");
        FallbackLabel = text.Get("profiles.fallback");
        SaveDraftLabel = text.Get("profiles.saveDraft");
        ApplyLabel = text.Get("profiles.apply");
        CancelLabel = text.Get("profiles.cancel");
        DeleteLabel = text.Get("profiles.delete");
        ConfirmDeleteLabel = text.Get("profiles.confirmDelete");
        KeepLabel = text.Get("profiles.keep");
        PreviewLabel = text.Get("profiles.preview");
        NewCommand = new ActionCommand(BeginNewProfile);
        SaveDraftCommand = new ActionCommand(SaveToDraft);
        CancelCommand = new ActionCommand(Cancel);
        CancelEditorCommand = new ActionCommand(CancelEditorChanges);
        RequestDeleteCommand = new ActionCommand(RequestDelete);
        ConfirmDeleteCommand = new ActionCommand(ConfirmDelete);
        KeepCommand = new ActionCommand(() => DeleteConfirmationVisible = false);
        RefreshFromDraft();
    }

    public string Eyebrow { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string ListLabel { get; }
    public string EmptyLabel { get; }
    public string AddLabel { get; }
    public string AliasLabel { get; }
    public string UsbLabel { get; }
    public string MdnsLabel { get; }
    public string PairingLabel { get; }
    public string ConnectionLabel { get; }
    public string TransportLabel { get; }
    public string FallbackLabel { get; }
    public string SaveDraftLabel { get; }
    public string ApplyLabel { get; }
    public string CancelLabel { get; }
    public string DeleteLabel { get; }
    public string ConfirmDeleteLabel { get; }
    public string KeepLabel { get; }
    public string PreviewLabel { get; }
    public ObservableCollection<SavedProfileChoice> Profiles { get; }
    public IReadOnlyList<ProfileTransportChoice> TransportChoices { get; }
    public ICommand NewCommand { get; }
    public ICommand SaveDraftCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand CancelEditorCommand { get; }
    public ICommand RequestDeleteCommand { get; }
    public ICommand ConfirmDeleteCommand { get; }
    public ICommand KeepCommand { get; }
    public bool IsPreview => session is null;
    public bool CanEdit => session is null || session.Draft is not null && !session.IsBusy && !session.RequiresReload;
    public bool IsEditing => editingId.HasValue;
    public bool HasNoProfiles => Profiles.Count == 0;
    public bool HasValidation => ValidationMessage is not null;
    public bool CanSaveDraft => CanEdit && IsEditing && !HasValidation && HasUnstagedChanges && !isApplying;
    public bool CanApply => CanEdit && !IsPreview && !HasValidation && IsDirty && !isApplying;
    public bool CanCancel => CanEdit && IsDirty && !isApplying;
    public bool CanDelete => CanEdit && selectedBaseline is not null && !isApplying;
    public bool HasUnstagedChanges => IsEditing && !EditorMatchesSelectedBaseline();
    public bool IsDirty => HasUnstagedChanges || (session?.IsDirty ??
        !previewDraft.SequenceEqual(previewBaseline));
    public string? EditingId => editingId?.ToString();

    /// <summary>Routes group Apply and Cancel through the owner that synchronizes options and profiles.</summary>
    public void AttachWorkspace(ConfigurationWorkspaceViewModel configuration)
    {
        workspace = configuration;
        configuration.AttachProfileEditor(this);
        configuration.PropertyChanged += OnWorkspaceChanged;
        NotifyActionState();
    }

    /// <summary>Refreshes profile action availability when the shared authority changes.</summary>
    private void OnWorkspaceChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(ConfigurationWorkspaceViewModel.CanEdit) or
            nameof(ConfigurationWorkspaceViewModel.Status) or nameof(ConfigurationWorkspaceViewModel.IsDirty))
        {
            NotifyActionState();
        }
    }

    public SavedProfileChoice? SelectedProfile
    {
        get => selectedProfile;
        set
        {
            if (selectedProfile == value)
            {
                return;
            }

            if (HasUnstagedChanges)
            {
                StatusMessage = text.Get("profiles.saveOrCancelFirst");
                OnPropertyChanged();
                return;
            }

            SetProperty(ref selectedProfile, value);
            LoadEditor(value is null ? null : CurrentProfiles().FirstOrDefault(profile => profile.Id == value.Id));
        }
    }

    public string Alias { get => alias; set => ChangeText(ref alias, value); }
    public string UsbSerial { get => usbSerial; set => ChangeText(ref usbSerial, value); }
    public string MdnsService { get => mdnsService; set => ChangeText(ref mdnsService, value); }
    public string PairingEndpoint { get => pairingEndpoint; set => ChangeText(ref pairingEndpoint, value); }
    public string ConnectionEndpoint { get => connectionEndpoint; set => ChangeText(ref connectionEndpoint, value); }

    public ProfileTransportChoice SelectedTransport
    {
        get => selectedTransport;
        set
        {
            if (value is not null && SetProperty(ref selectedTransport, value))
            {
                ValidateEditor();
            }
        }
    }

    public bool AllowFallback
    {
        get => allowFallback;
        set
        {
            if (SetProperty(ref allowFallback, value))
            {
                ValidateEditor();
            }
        }
    }

    public bool DeleteConfirmationVisible
    {
        get => deleteConfirmationVisible;
        private set => SetProperty(ref deleteConfirmationVisible, value);
    }

    public string? ValidationMessage
    {
        get => validationMessage;
        private set
        {
            if (SetProperty(ref validationMessage, value))
            {
                OnPropertyChanged(nameof(HasValidation));
            }
        }
    }

    public string? StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    /// <summary>Refreshes the list after the owner loads or applies the canonical session.</summary>
    public void RefreshFromDraft()
    {
        ProfileId? previousId = editingId;
        Profiles.Clear();

        foreach (DeviceProfile profile in CurrentProfiles())
        {
            Profiles.Add(new SavedProfileChoice(profile.Id, LabelFor(profile)));
        }

        OnPropertyChanged(nameof(HasNoProfiles));
        SavedProfileChoice? next = Profiles.FirstOrDefault(profile => profile.Id == previousId) ?? Profiles.FirstOrDefault();
        selectedProfile = next;
        OnPropertyChanged(nameof(SelectedProfile));
        LoadEditor(next is null ? null : CurrentProfiles().First(profile => profile.Id == next.Id));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanEdit));
    }

    /// <summary>Stages a new stable profile identity without reading devices or writing files.</summary>
    public void BeginNewProfile()
    {
        if (!CanEdit)
        {
            return;
        }

        if (HasUnstagedChanges)
        {
            StatusMessage = text.Get("profiles.saveOrCancelFirst");
            return;
        }

        selectedProfile = null;
        OnPropertyChanged(nameof(SelectedProfile));
        selectedBaseline = null;
        editingId = ProfileId.New();
        editingDeviceId = DeviceId.New();
        LoadFields(null);
        StatusMessage = null;
        ValidateEditor();
    }

    /// <summary>Stages the current editor from a button without writing persistent state.</summary>
    public void SaveToDraft() => TrySaveToDraft();

    /// <summary>Copies a valid editor into the detached draft and reports whether staging succeeded.</summary>
    public bool TrySaveToDraft()
    {
        if (!CanEdit)
        {
            return false;
        }

        if (!TryBuildProfile(out DeviceProfile? profile, out string? error))
        {
            ValidationMessage = error;
            return false;
        }

        if (!CanSaveDraft)
        {
            return !HasUnstagedChanges;
        }

        if (session is null)
        {
            int index = previewDraft.FindIndex(existing => existing.Id == profile.Id);

            if (index < 0)
            {
                previewDraft.Add(profile);
            }
            else
            {
                previewDraft[index] = profile;
            }
        }
        else
        {
            session.UpsertProfile(profile);
        }

        RefreshFromDraft();
        StatusMessage = text.Get("profiles.staged");
        return true;
    }

    /// <summary>Applies the shared valid v2 draft through the revision-aware session.</summary>
    public async Task<ConfigurationSessionApplyResult?> ApplyAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApply)
        {
            return null;
        }

        if (HasUnstagedChanges && !TrySaveToDraft())
        {
            return null;
        }

        isApplying = true;
        NotifyActionState();

        try
        {
            ConfigurationSessionApplyResult result = workspace is null
                ? await session!.ApplyAsync(cancellationToken)
                : await workspace.ApplyAsync(cancellationToken);
            StatusMessage = text.Get(result.Status switch
            {
                ConfigurationSessionApplyStatus.Applied => "profiles.applied",
                ConfigurationSessionApplyStatus.RevisionConflict => "profiles.conflict",
                ConfigurationSessionApplyStatus.Invalid => "profiles.invalidDraft",
                ConfigurationSessionApplyStatus.NoChanges => "profiles.noChanges",
                _ => "profiles.applyFailed",
            });

            if (result.Status == ConfigurationSessionApplyStatus.Applied)
            {
                RefreshFromDraft();
            }

            return result;
        }
        finally
        {
            isApplying = false;
            NotifyActionState();
        }
    }

    /// <summary>Discards unstaged editor input and the detached session draft.</summary>
    public void Cancel()
    {
        if (!CanEdit)
        {
            return;
        }

        if (session is null)
        {
            previewDraft.Clear();
            previewDraft.AddRange(previewBaseline);
        }
        else
        {
            if (workspace is null)
            {
                session.CancelChanges();
            }
            else
            {
                workspace.CancelChanges();
            }
        }

        DeleteConfirmationVisible = false;
        RefreshFromDraft();
        StatusMessage = text.Get("profiles.cancelled");
    }

    /// <summary>Discards only visible editor input while retaining other staged draft edits.</summary>
    public void CancelEditorChanges()
    {
        if (selectedBaseline is null)
        {
            RefreshFromDraft();
            return;
        }

        LoadEditor(selectedBaseline);
        StatusMessage = text.Get("profiles.editorCancelled");
    }

    /// <summary>Requires a second explicit action before deleting a saved profile.</summary>
    private void RequestDelete()
    {
        if (HasUnstagedChanges)
        {
            StatusMessage = text.Get("profiles.saveOrCancelFirst");
            return;
        }

        if (CanDelete)
        {
            DeleteConfirmationVisible = true;
        }
    }

    /// <summary>Stages deletion without touching persisted configuration until Apply.</summary>
    private void ConfirmDelete()
    {
        if (!DeleteConfirmationVisible || !CanDelete || selectedBaseline is null || HasUnstagedChanges)
        {
            return;
        }

        if (session is null)
        {
            previewDraft.RemoveAll(profile => profile.Id == selectedBaseline.Id);
        }
        else
        {
            session.DeleteProfile(selectedBaseline.Id);
        }

        DeleteConfirmationVisible = false;
        selectedBaseline = null;
        editingId = null;
        RefreshFromDraft();
        StatusMessage = text.Get("profiles.deletedDraft");
    }

    /// <summary>Copies a selected profile into editable text fields without changing the draft.</summary>
    private void LoadEditor(DeviceProfile? profile)
    {
        selectedBaseline = profile;
        editingId = profile?.Id;
        editingDeviceId = profile?.DeviceIdentity;
        DeleteConfirmationVisible = false;
        LoadFields(profile);
        ValidateEditor();
    }

    /// <summary>Loads each visible field while retaining canonical endpoint formatting.</summary>
    private void LoadFields(DeviceProfile? profile)
    {
        alias = profile?.Alias ?? string.Empty;
        usbSerial = profile?.UsbIdentity?.Value ?? string.Empty;
        mdnsService = profile?.MdnsIdentity?.Value ?? string.Empty;
        pairingEndpoint = profile?.PairingEndpoint?.ToString() ?? string.Empty;
        connectionEndpoint = profile?.ConnectionEndpoint?.ToString() ?? string.Empty;
        selectedTransport = TransportChoices.First(choice => choice.Value ==
            (profile?.Connection.PreferredTransport ?? TransportPreference.Automatic));
        allowFallback = profile?.Connection.AllowFallback ?? true;
        OnPropertyChanged(nameof(Alias));
        OnPropertyChanged(nameof(UsbSerial));
        OnPropertyChanged(nameof(MdnsService));
        OnPropertyChanged(nameof(PairingEndpoint));
        OnPropertyChanged(nameof(ConnectionEndpoint));
        OnPropertyChanged(nameof(SelectedTransport));
        OnPropertyChanged(nameof(AllowFallback));
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(EditingId));
    }

    /// <summary>Validates visible raw input before constructing a profile for staging.</summary>
    private bool TryBuildProfile([NotNullWhen(true)] out DeviceProfile? profile, out string? error)
    {
        profile = null;
        error = null;

        if (!editingId.HasValue)
        {
            return false;
        }

        bool invalidAlias = alias.Length > 128 || (alias.Length > 0 &&
            (string.IsNullOrWhiteSpace(alias) || alias.Any(char.IsControl)));
        bool invalidUsb = usbSerial.Length > 0 &&
            (string.IsNullOrWhiteSpace(usbSerial) || usbSerial.Any(char.IsControl));
        bool invalidMdns = mdnsService.Length > 0 &&
            (string.IsNullOrWhiteSpace(mdnsService) || mdnsService.Any(char.IsControl));

        if (invalidAlias || invalidUsb || invalidMdns)
        {
            error = text.Get("profiles.validation.identity");
            return false;
        }

        if (pairingEndpoint.Length > 0 && !NetworkEndpoint.TryParse(pairingEndpoint, out _))
        {
            error = text.Get("profiles.validation.pairing");
            return false;
        }

        if (connectionEndpoint.Length > 0 && !NetworkEndpoint.TryParse(connectionEndpoint, out _))
        {
            error = text.Get("profiles.validation.connection");
            return false;
        }

        profile = new DeviceProfile
        {
            Id = editingId.Value,
            DeviceIdentity = editingDeviceId,
            Alias = alias.Length == 0 ? null : alias,
            UsbIdentity = usbSerial.Length == 0 ? null : new UsbSerial(usbSerial),
            MdnsIdentity = mdnsService.Length == 0 ? null : new MdnsServiceName(mdnsService),
            PairingEndpoint = pairingEndpoint.Length == 0 ? null : NetworkEndpoint.Parse(pairingEndpoint),
            ConnectionEndpoint = connectionEndpoint.Length == 0 ? null : NetworkEndpoint.Parse(connectionEndpoint),
            Connection = new ConnectionPreferences
            {
                PreferredTransport = selectedTransport.Value,
                AllowFallback = allowFallback,
            },
        };

        if (profile.Validate("Profile").Count != 0)
        {
            error = text.Get("profiles.validation.profile");
            profile = null;
            return false;
        }

        return true;
    }

    /// <summary>Updates one editor field without discarding invalid raw input.</summary>
    private void ChangeText(ref string field, string? value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (SetProperty(ref field, value ?? string.Empty, name))
        {
            ValidateEditor();
        }
    }

    /// <summary>Refreshes semantic validation and action availability after each edit.</summary>
    private void ValidateEditor()
    {
        TryBuildProfile(out _, out string? error);
        ValidationMessage = error;

        if (HasUnstagedChanges)
        {
            DeleteConfirmationVisible = false;
        }

        NotifyActionState();
    }

    /// <summary>Notifies bindings whose state depends on the current editor or session.</summary>
    private void NotifyActionState()
    {
        OnPropertyChanged(nameof(HasUnstagedChanges));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanSaveDraft));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(CanEdit));
    }

    /// <summary>Returns the detached session profiles or the isolated preview profiles.</summary>
    private IReadOnlyList<DeviceProfile> CurrentProfiles() => session?.Draft?.Profiles ?? previewDraft;

    /// <summary>Presents a saved label without claiming a live connection.</summary>
    private string LabelFor(DeviceProfile profile) => profile.Alias ??
        profile.UsbIdentity?.Value ?? profile.MdnsIdentity?.Value ??
        profile.ConnectionEndpoint?.ToString() ?? profile.Id.ToString();

    /// <summary>Compares visible fields to the staged profile before switching selection.</summary>
    private bool EditorMatchesSelectedBaseline() => selectedBaseline is not null &&
        alias == (selectedBaseline.Alias ?? string.Empty) &&
        usbSerial == (selectedBaseline.UsbIdentity?.Value ?? string.Empty) &&
        mdnsService == (selectedBaseline.MdnsIdentity?.Value ?? string.Empty) &&
        pairingEndpoint == (selectedBaseline.PairingEndpoint?.ToString() ?? string.Empty) &&
        connectionEndpoint == (selectedBaseline.ConnectionEndpoint?.ToString() ?? string.Empty) &&
        selectedTransport.Value == selectedBaseline.Connection.PreferredTransport &&
        allowFallback == selectedBaseline.Connection.AllowFallback;
}
