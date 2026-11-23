using System.Collections.ObjectModel;
using System.Windows.Input;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Application.Activation;
using ScrcpySeamless.Core.Application.NativeHost;

namespace ScrcpySeamless.Desktop.Presentation;

/// <summary>Owns bounded independent child entries; selection never transfers lifetime ownership.</summary>
public sealed class SessionsWorkspaceViewModel : ObservableViewModel
{
    public const int MaximumEntries = 8;
    private readonly ControlCenterSessions registry = new();
    private readonly Func<DeviceSessionViewModel> createSession;
    private readonly Action<DeviceSessionViewModel?> selectSession;
    private readonly PresentationText text;
    private readonly ObservableCollection<DeviceSessionViewModel> entries = [];
    private DeviceSessionViewModel? selectedSession;
    private bool shuttingDown;
    private string status = string.Empty;

    public SessionsWorkspaceViewModel(Func<DeviceSessionViewModel> createSession,
        Action<DeviceSessionViewModel?> selectSession, PresentationText text)
    {
        this.createSession = createSession;
        this.selectSession = selectSession;
        this.text = text;
        Entries = new ReadOnlyObservableCollection<DeviceSessionViewModel>(entries);
        NewSessionCommand = new ActionCommand(() => AddSession());
        RemoveSessionCommand = new ActionCommand(() => RemoveSelected());
        AddSession();
    }

    public ReadOnlyObservableCollection<DeviceSessionViewModel> Entries { get; }
    public ICommand NewSessionCommand { get; }
    public ICommand RemoveSessionCommand { get; }
    public string Title => text.Get("sessions.title");
    public string Description => text.Get("sessions.description");
    public string NewLabel => text.Get("sessions.new");
    public string RemoveLabel => text.Get("sessions.remove");
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public bool CanAdd => !shuttingDown && Entries.Count < MaximumEntries;
    public bool CanRemove => !shuttingDown && SelectedSession is { HasOwnedSession: false };

    public DeviceSessionViewModel? SelectedSession
    {
        get => selectedSession;
        set
        {
            if (value is null && Entries.Count > 0 || value is not null && !Entries.Contains(value))
            {
                return;
            }

            if (!SetProperty(ref selectedSession, value))
            {
                return;
            }

            selectSession(value);

            OnPropertyChanged(nameof(CanRemove));
        }
    }

    public DeviceSessionViewModel? AddSession()
    {
        if (!CanAdd)
        {
            Status = text.Get("sessions.limit");
            return null;
        }

        DeviceSessionViewModel entry = createSession();
        entry.AttachOwnershipRegistry(registry);
        entry.PropertyChanged += EntryChanged;
        entries.Add(entry);
        SelectedSession = entry;
        Status = string.Empty;
        OnPropertyChanged(nameof(CanAdd));
        return entry;
    }

    public bool RemoveSelected()
    {
        if (!CanRemove || SelectedSession is not { } entry)
        {
            return false;
        }

        entry.PropertyChanged -= EntryChanged;
        entries.Remove(entry);
        SelectedSession = Entries.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedSession));
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(CanRemove));
        return true;
    }

    private void EntryChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(DeviceSessionViewModel.HasOwnedSession))
        {
            OnPropertyChanged(nameof(CanRemove));
        }
    }

    public Task<NativeFocusOutcome> FocusSessionAsync(SessionId sessionId, CancellationToken cancellationToken = default)
    {
        DeviceSessionViewModel? entry = Entries.FirstOrDefault(item => item.CurrentSessionId == sessionId);
        return entry?.FocusSessionAsync(sessionId, cancellationToken)
            ?? Task.FromResult(NativeFocusOutcome.InvalidState);
    }

    public async Task<bool> ShutdownAsync()
    {
        shuttingDown = true;
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(CanRemove));
        bool[] settled = await Task.WhenAll(Entries.Select(SettleEntryAsync));
        bool success = settled.All(item => item);
        Status = success ? text.Get("sessions.closed") : text.Get("sessions.cleanupPending");
        return success;
    }

    private static async Task<bool> SettleEntryAsync(DeviceSessionViewModel entry)
    {
        try
        {
            return await entry.ShutdownAsync();
        }
        catch
        {
            // An unexpected adapter fault never releases the retained entry.
            return false;
        }
    }

    public void ResumeAfterFailedShutdown()
    {
        shuttingDown = false;
        foreach (DeviceSessionViewModel entry in Entries)
        {
            entry.ResumeAfterFailedShutdown();
        }

        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(CanRemove));
    }
}
