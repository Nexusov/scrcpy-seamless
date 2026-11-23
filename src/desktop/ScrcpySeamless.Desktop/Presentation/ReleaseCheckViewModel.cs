using System.Globalization;
using System.Windows.Input;
using ScrcpySeamless.Core.Application.Updates;

namespace ScrcpySeamless.Desktop.Presentation;

public sealed record ReleaseChannelChoice(ReleaseChannel Channel, string Label);

/// <summary>One explicit bounded check; construction and navigation perform no network request.</summary>
public sealed class ReleaseCheckViewModel : ObservableViewModel
{
    private const string ReleasePath = "/Nexusov/scrcpy-seamless/releases/tag/";
    private readonly IReleaseChecker checker;
    private readonly PresentationText text;
    private readonly Func<Uri, CancellationToken, Task> openReleaseLink;
    private readonly AsyncActionCommand checkCommand;
    private readonly AsyncActionCommand cancelCommand;
    private readonly AsyncActionCommand openCommand;
    private CancellationTokenSource? requestCancellation;
    private CancellationTokenSource? navigationCancellation;
    private Task activeCheck = Task.CompletedTask;
    private Task activeNavigation = Task.CompletedTask;
    private ReleaseChannelChoice channel;
    private ReleaseCheckStatus? resultStatus;
    private Uri? releaseUri;
    private string releaseVersion = string.Empty;
    private bool checking;
    private bool cancelling;
    private bool opening;
    private bool closed;

    public ReleaseCheckViewModel(IReleaseChecker checker, string currentProductVersion,
        PresentationText text, Func<Uri, CancellationToken, Task> openReleaseLink)
    {
        this.checker = checker;
        this.text = text;
        this.openReleaseLink = openReleaseLink;
        CurrentVersion = currentProductVersion;
        Channels = Array.AsReadOnly(new[]
        {
            new ReleaseChannelChoice(ReleaseChannel.Stable, text.Get("releaseCheck.channel.stable")),
            new ReleaseChannelChoice(ReleaseChannel.Prerelease, text.Get("releaseCheck.channel.prerelease")),
        });
        channel = Channels[0];
        checkCommand = new AsyncActionCommand(CheckAsync, () => CanCheck, ReportUnexpectedFailure);
        cancelCommand = new AsyncActionCommand(() => { CancelCheck(); return Task.CompletedTask; },
            () => CanCancel, ReportUnexpectedFailure);
        openCommand = new AsyncActionCommand(OpenReleaseAsync, () => CanOpenRelease, ReportUnexpectedFailure);

        if (!SemanticVersion.TryParse(CurrentVersion, out _))
        {
            resultStatus = ReleaseCheckStatus.InvalidRequest;
        }
    }

    public string CurrentVersion { get; }
    public string Title => text.Get("releaseCheck.title");
    public string Description => text.Get("releaseCheck.description");
    public string CurrentVersionLabel => string.Format(CultureInfo.CurrentCulture, text.Get("releaseCheck.current"), CurrentVersion);
    public string ChannelLabel => text.Get("releaseCheck.channel");
    public string CheckLabel => text.Get("releaseCheck.check");
    public string CancelLabel => text.Get("releaseCheck.cancel");
    public string OpenLabel => text.Get("releaseCheck.open");
    public string ReleaseVersion => releaseVersion;
    public bool HasReleaseVersion => releaseVersion.Length != 0;
    public IReadOnlyList<ReleaseChannelChoice> Channels { get; }
    public ICommand CheckCommand => checkCommand;
    public ICommand CancelCommand => cancelCommand;
    public ICommand OpenReleaseCommand => openCommand;
    public bool IsChecking => checking;
    public bool CanChooseChannel => !checking && !opening && !closed;
    public bool CanCheck => !closed && !checking && !opening && SemanticVersion.TryParse(CurrentVersion, out _);
    public bool CanCancel => checking && !cancelling && !closed;
    public bool CanOpenRelease => !closed && !checking && !opening && releaseUri is not null;
    public ReleaseCheckStatus? ResultStatus => resultStatus;
    public string Status => text.Get(checking
        ? cancelling ? "releaseCheck.cancelling" : "releaseCheck.checking"
        : resultStatus is { } status ? $"releaseCheck.status.{status}" : "releaseCheck.idle");

    public ReleaseChannelChoice SelectedChannel
    {
        get => channel;
        set
        {
            if (CanChooseChannel && Channels.Contains(value))
            {
                SetProperty(ref channel, value);
            }
        }
    }

    /// <summary>Repeated command delivery joins the active request, never starts another check.</summary>
    public Task CheckAsync()
    {
        if (checking)
        {
            return activeCheck;
        }

        if (!CanCheck)
        {
            return Task.CompletedTask;
        }
        CancellationTokenSource cancellation = new();
        requestCancellation = cancellation;
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        activeCheck = completion.Task;
        checking = true;
        cancelling = false;
        releaseUri = null;
        releaseVersion = string.Empty;
        resultStatus = null;
        NotifyState();
        _ = CompleteCheckAsync(cancellation, SelectedChannel.Channel, completion);
        return completion.Task;
    }

    private async Task CompleteCheckAsync(CancellationTokenSource cancellation,
        ReleaseChannel selectedChannel, TaskCompletionSource completion)
    {
        try
        {
            await RunCheckAsync(cancellation, selectedChannel);
            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    public void CancelCheck()
    {
        if (!CanCancel)
        {
            return;
        }
        cancelling = true;
        NotifyState();
        requestCancellation?.Cancel();
    }

    private async Task RunCheckAsync(CancellationTokenSource cancellation, ReleaseChannel selectedChannel)
    {
        ReleaseCheckResult result;

        try
        {
            result = cancellation.IsCancellationRequested ? new(ReleaseCheckStatus.Cancelled)
                : await checker.CheckAsync(new(CurrentVersion, selectedChannel), cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            result = new(ReleaseCheckStatus.Cancelled);
        }
        catch (Exception)
        {
            result = new(ReleaseCheckStatus.Unavailable);
        }

        if (!closed && !cancellation.IsCancellationRequested)
        {
            bool hasRelease = result.Status is ReleaseCheckStatus.UpdateAvailable or ReleaseCheckStatus.NoNewerRelease;
            bool valid = Enum.IsDefined(result.Status) && (!hasRelease || IsTrustedRelease(result));
            resultStatus = valid ? result.Status : ReleaseCheckStatus.InvalidResponse;

            if (valid && hasRelease)
            {
                releaseUri = result.ReleaseUri;
                releaseVersion = result.Version!;
            }
        }
        else
        {
            resultStatus = ReleaseCheckStatus.Cancelled;
        }
        requestCancellation = null;
        cancellation.Dispose();
        checking = false;
        cancelling = false;
        NotifyState();
    }

    /// <summary>Only a click can hand a validated repository release URL to the platform adapter.</summary>
    public Task OpenReleaseAsync()
    {
        if (!CanOpenRelease)
        {
            return activeNavigation;
        }
        Uri uri = releaseUri!;
        CancellationTokenSource cancellation = new();
        navigationCancellation = cancellation;
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        activeNavigation = completion.Task;
        opening = true;
        NotifyState();
        _ = CompleteNavigationAsync(uri, cancellation, completion);
        return completion.Task;
    }

    private async Task CompleteNavigationAsync(Uri uri, CancellationTokenSource cancellation,
        TaskCompletionSource completion)
    {
        try
        {
            await RunNavigationAsync(uri, cancellation);
            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    private async Task RunNavigationAsync(Uri uri, CancellationTokenSource cancellation)
    {
        try
        {
            if (!cancellation.IsCancellationRequested)
            {
                await openReleaseLink(uri, cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (!closed)
            {
                resultStatus = ReleaseCheckStatus.Unavailable;
            }
        }
        navigationCancellation = null;
        cancellation.Dispose();
        opening = false;
        NotifyState();
    }

    /// <summary>The composition must await this before disposing the checker or releasing the window owner.</summary>
    public async Task CloseAsync()
    {
        if (!closed)
        {
            closed = true;
            requestCancellation?.Cancel();
            navigationCancellation?.Cancel();
            releaseUri = null;
            NotifyState();
        }

        await Task.WhenAll(activeCheck, activeNavigation);
    }

    private static bool IsTrustedRelease(ReleaseCheckResult result)
    {
        Uri? uri = result.ReleaseUri;

        if (!SemanticVersion.TryParse(result.Version, out _) || uri is null || !uri.IsAbsoluteUri ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Host != "github.com" || !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            !uri.AbsolutePath.StartsWith(ReleasePath, StringComparison.Ordinal))
        {
            return false;
        }
        string tag = Uri.UnescapeDataString(uri.AbsolutePath[ReleasePath.Length..]);
        string version = tag.StartsWith('v') ? tag[1..] : tag;
        Uri expected = new("https://github.com" + ReleasePath + Uri.EscapeDataString(tag));
        return version == result.Version && uri.AbsoluteUri == expected.AbsoluteUri;
    }

    private void ReportUnexpectedFailure(Exception exception)
    {
        _ = exception;
        resultStatus = ReleaseCheckStatus.Unavailable;
        NotifyState();
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(ResultStatus));
        OnPropertyChanged(nameof(IsChecking));
        OnPropertyChanged(nameof(CanCheck));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanChooseChannel));
        OnPropertyChanged(nameof(CanOpenRelease));
        OnPropertyChanged(nameof(ReleaseVersion));
        OnPropertyChanged(nameof(HasReleaseVersion));
        checkCommand.NotifyCanExecuteChanged();
        cancelCommand.NotifyCanExecuteChanged();
        openCommand.NotifyCanExecuteChanged();
    }
}
