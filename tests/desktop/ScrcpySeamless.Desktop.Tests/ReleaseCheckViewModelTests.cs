using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using ScrcpySeamless.Core.Application.Updates;
using ScrcpySeamless.Desktop.Presentation;
using ScrcpySeamless.Desktop.Views;
using Xunit;

namespace ScrcpySeamless.Desktop.Tests;

public sealed class ReleaseCheckViewModelTests
{
    private const string CurrentVersion = "2.0.0-rc.1+synthetic";
    private static readonly ReleaseCheckResult NewRelease = new(ReleaseCheckStatus.UpdateAvailable,
        "2.0.0", new Uri("https://github.com/Nexusov/scrcpy-seamless/releases/tag/v2.0.0"));

    [Fact]
    public async Task ConstructionAndChannelChoiceAreInertAndVersionIsSupplied()
    {
        FakeChecker checker = new();
        ReleaseCheckViewModel model = Create(checker);
        Assert.Equal(0, checker.Calls);
        Assert.Null(model.ResultStatus);
        Assert.False(model.CanOpenRelease);
        Assert.Contains(CurrentVersion, model.CurrentVersionLabel);
        model.SelectedChannel = model.Channels[1];
        Assert.Equal(0, checker.Calls);
        checker.Response = Task.FromResult(new ReleaseCheckResult(ReleaseCheckStatus.NoEligibleRelease));
        await model.CheckAsync();
        Assert.Equal(new ReleaseCheckRequest(CurrentVersion, ReleaseChannel.Prerelease), checker.Request);
        Assert.Contains("latest 30", model.Status);
        await model.CloseAsync();
    }

    [Fact]
    public async Task RepeatedCheckJoinsOneRequestAndCancelWaitsForActualSettlement()
    {
        FakeChecker checker = new();
        TaskCompletionSource<ReleaseCheckResult> response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        checker.Response = response.Task;
        ReleaseCheckViewModel model = Create(checker);
        Task check = model.CheckAsync();
        Assert.Same(check, model.CheckAsync());
        Assert.Equal(1, checker.Calls);
        Assert.False(model.CanChooseChannel);
        model.SelectedChannel = model.Channels[1];
        Assert.Equal(ReleaseChannel.Stable, model.SelectedChannel.Channel);
        model.CancelCheck();
        Assert.True(checker.Token.IsCancellationRequested);
        Assert.True(model.IsChecking);
        Assert.False(check.IsCompleted);
        Assert.False(model.CanCheck);
        response.SetResult(NewRelease);
        await check;
        Assert.Equal(ReleaseCheckStatus.Cancelled, model.ResultStatus);
        Assert.False(model.CanOpenRelease);
        Assert.False(model.HasReleaseVersion);
        await model.CloseAsync();
    }

    [Fact]
    public async Task CloseCancelsAndJoinsWithoutPublishingALateRelease()
    {
        FakeChecker checker = new();
        TaskCompletionSource<ReleaseCheckResult> response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        checker.Response = response.Task;
        ReleaseCheckViewModel model = Create(checker);
        Task check = model.CheckAsync();
        Task close = model.CloseAsync();
        Assert.True(checker.Token.IsCancellationRequested);
        Assert.False(close.IsCompleted);
        response.SetResult(NewRelease);
        await Task.WhenAll(check, close);
        Assert.False(model.CanCheck);
        Assert.False(model.CanOpenRelease);
        Assert.Equal(ReleaseCheckStatus.Cancelled, model.ResultStatus);
    }

    [Fact]
    public async Task ReleaseLinkRequiresASeparateClickAndCloseJoinsNavigation()
    {
        FakeChecker checker = new() { Response = Task.FromResult(NewRelease) };
        TaskCompletionSource navigation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int opened = 0;
        CancellationToken observedToken = default;
        ReleaseCheckViewModel model = new(checker, CurrentVersion, new PresentationText(), (uri, token) =>
        {
            Assert.Equal(NewRelease.ReleaseUri, uri);
            observedToken = token;
            opened++;
            return navigation.Task;
        });
        await model.CheckAsync();
        Assert.Equal(0, opened);
        Assert.True(model.CanOpenRelease);
        Task opening = model.OpenReleaseAsync();
        Assert.Same(opening, model.OpenReleaseAsync());
        Assert.Equal(1, opened);
        Task close = model.CloseAsync();
        Assert.True(observedToken.IsCancellationRequested);
        Assert.False(close.IsCompleted);
        navigation.SetResult();
        await Task.WhenAll(opening, close);
        Assert.False(model.CanOpenRelease);
    }

    [Fact]
    public async Task NoNewerResultDescribesOnlyTheCheckedWindow()
    {
        FakeChecker checker = new()
        {
            Response = Task.FromResult(new ReleaseCheckResult(ReleaseCheckStatus.NoNewerRelease,
                "1.9.0", new Uri("https://github.com/Nexusov/scrcpy-seamless/releases/tag/v1.9.0"))),
        };
        ReleaseCheckViewModel model = Create(checker);
        await model.CheckAsync();
        Assert.Contains("latest 30", model.Status);
        Assert.Contains("older entries were not searched", model.Status);
        Assert.Equal("1.9.0", model.ReleaseVersion);
        Assert.True(model.CanOpenRelease);
        await model.CloseAsync();
    }

    [Fact]
    public async Task PendingNavigationCannotBeOvertakenByAnotherCheck()
    {
        FakeChecker checker = new() { Response = Task.FromResult(NewRelease) };
        TaskCompletionSource navigation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ReleaseCheckViewModel model = new(checker, CurrentVersion, new PresentationText(), (_, _) => navigation.Task);
        await model.CheckAsync();
        Task opening = model.OpenReleaseAsync();
        bool checkWasAvailable = model.CheckCommand.CanExecute(null);
        model.SelectedChannel = model.Channels[1];
        checker.Response = Task.FromResult(new ReleaseCheckResult(ReleaseCheckStatus.UpdateAvailable,
            "2.1.0", new Uri("https://github.com/Nexusov/scrcpy-seamless/releases/tag/v2.1.0")));
        await model.CheckAsync();
        ReleaseCheckStatus? beforeNavigationFailure = model.ResultStatus;
        navigation.SetException(new IOException("synthetic navigation failure"));
        await opening;
        Console.WriteLine($"checkAvailable={checkWasAvailable}, checks={checker.Calls}, before={beforeNavigationFailure}, after={model.ResultStatus}, version={model.ReleaseVersion}");
        Assert.False(checkWasAvailable);
        Assert.Equal(1, checker.Calls);
        Assert.Equal(ReleaseChannel.Stable, model.SelectedChannel.Channel);
        Assert.Equal(ReleaseCheckStatus.Unavailable, model.ResultStatus);
        await model.CloseAsync();
    }

    [Fact]
    public async Task UnknownTypedResultCannotSelectAnUnavailableResourceOrLink()
    {
        FakeChecker checker = new() { Response = Task.FromResult(NewRelease with { Status = (ReleaseCheckStatus) ushort.MaxValue }) };
        ReleaseCheckViewModel model = Create(checker);
        await model.CheckAsync();
        Assert.Equal(ReleaseCheckStatus.InvalidResponse, model.ResultStatus);
        Assert.False(model.CanOpenRelease);
        Assert.False(model.HasReleaseVersion);
        await model.CloseAsync();
    }

    [Theory]
    [InlineData("http://github.com/Nexusov/scrcpy-seamless/releases/tag/v2.0.0")]
    [InlineData("https://example.invalid/Nexusov/scrcpy-seamless/releases/tag/v2.0.0")]
    [InlineData("https://github.com/other/repo/releases/tag/v2.0.0")]
    [InlineData("https://github.com/Nexusov/scrcpy-seamless/releases/tag/v3.0.0")]
    [InlineData("https://github.com/Nexusov/scrcpy-seamless/releases/tag/v2.0.0?next=other")]
    public async Task InvalidResultCannotBecomeANavigationTarget(string address)
    {
        FakeChecker checker = new()
        {
            Response = Task.FromResult(NewRelease with { ReleaseUri = new Uri(address) }),
        };
        ReleaseCheckViewModel model = Create(checker);
        await model.CheckAsync();
        Assert.Equal(ReleaseCheckStatus.InvalidResponse, model.ResultStatus);
        Assert.False(model.CanOpenRelease);
        await model.OpenReleaseAsync();
        await model.CloseAsync();
    }

    [Theory]
    [InlineData(ReleaseCheckStatus.NoEligibleRelease)]
    [InlineData(ReleaseCheckStatus.InvalidRequest)]
    [InlineData(ReleaseCheckStatus.Offline)]
    [InlineData(ReleaseCheckStatus.TimedOut)]
    [InlineData(ReleaseCheckStatus.Cancelled)]
    [InlineData(ReleaseCheckStatus.RateLimited)]
    [InlineData(ReleaseCheckStatus.Unavailable)]
    [InlineData(ReleaseCheckStatus.InvalidResponse)]
    [InlineData(ReleaseCheckStatus.ResponseTooLarge)]
    public async Task EveryTypedFailureOrUnknownStateUsesLocalizedCopy(ReleaseCheckStatus status)
    {
        FakeChecker checker = new() { Response = Task.FromResult(new ReleaseCheckResult(status)) };
        PresentationText text = new(new Dictionary<string, string> { [$"releaseCheck.status.{status}"] = "localized-result" });
        ReleaseCheckViewModel model = new(checker, CurrentVersion, text, (_, _) => throw new InvalidOperationException());
        await model.CheckAsync();
        Assert.Equal(status, model.ResultStatus);
        Assert.Equal("localized-result", model.Status);
        Assert.False(model.CanOpenRelease);
        await model.CloseAsync();
    }

    [Fact]
    public async Task ExceptionsAndInvalidAssemblyVersionAreBoundedStatuses()
    {
        FakeChecker checker = new() { Response = Task.FromException<ReleaseCheckResult>(new IOException("PRIVATE EXCEPTION")) };
        ReleaseCheckViewModel model = Create(checker);
        await model.CheckAsync();
        Assert.Equal(ReleaseCheckStatus.Unavailable, model.ResultStatus);
        Assert.DoesNotContain("PRIVATE", model.Status);
        await model.CloseAsync();
        ReleaseCheckViewModel invalid = new(checker, "invalid", new PresentationText(), (_, _) => Task.CompletedTask);
        Assert.False(invalid.CanCheck);
        await invalid.CheckAsync();
        Assert.Equal(1, checker.Calls);
        Assert.Equal(ReleaseCheckStatus.InvalidRequest, invalid.ResultStatus);
        await invalid.CloseAsync();
    }

    [Fact]
    public async Task ReentrantCloseObservesThePublishedRequestReceipt()
    {
        FakeChecker checker = new();
        ReleaseCheckViewModel model = Create(checker);
        Task? close = null;
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(model.IsChecking) && model.IsChecking && close is null)
            {
                close = model.CloseAsync();
            }
        };
        await model.CheckAsync();
        Assert.NotNull(close);
        await close;
        Assert.Equal(0, checker.Calls);
        Assert.Equal(ReleaseCheckStatus.Cancelled, model.ResultStatus);
    }

    [AvaloniaFact]
    public void DiagnosticsWithoutARuntimeShowsTheInertAccessibleCheck()
    {
        FakeChecker checker = new();
        ReleaseCheckViewModel releases = Create(checker);
        DiagnosticsWorkspaceViewModel diagnostics = new(null, new PresentationText(), releases);
        diagnostics.Refresh();
        Assert.Empty(diagnostics.Rows);
        Window window = new() { Content = new DiagnosticsView { DataContext = diagnostics }, Width = 740, Height = 540 };
        window.Show();
        window.UpdateLayout();
        Button check = Assert.Single(window.GetVisualDescendants().OfType<Button>(),
            button => AutomationProperties.GetAutomationId(button) == "releaseCheck.check");
        Assert.Equal("Check for releases", AutomationProperties.GetName(check));
        Assert.True(check.IsEnabled);
        Assert.Equal(0, checker.Calls);
        window.Close();
    }

    private static ReleaseCheckViewModel Create(FakeChecker checker) =>
        new(checker, CurrentVersion, new PresentationText(), (_, _) => throw new InvalidOperationException("No navigation expected."));

    private sealed class FakeChecker : IReleaseChecker
    {
        public int Calls { get; private set; }
        public ReleaseCheckRequest? Request { get; private set; }
        public CancellationToken Token { get; private set; }
        public Task<ReleaseCheckResult> Response { get; set; } = Task.FromResult(new ReleaseCheckResult(ReleaseCheckStatus.NoEligibleRelease));

        public Task<ReleaseCheckResult> CheckAsync(ReleaseCheckRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            Request = request;
            Token = cancellationToken;
            return Response;
        }
    }
}
