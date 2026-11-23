using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ScrcpySeamless.Core.Application.Updates;
using ScrcpySeamless.Infrastructure.Updates;
using Xunit;

namespace ScrcpySeamless.Infrastructure.Tests.Updates;

public sealed class GitHubReleaseCheckerTests
{
    private static readonly TimeSpan TestBudget = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private const string Release = "[{\"tag_name\":\"v2.0.1\",\"draft\":false,\"prerelease\":false}]";

    [Fact]
    public async Task RequestsOnlyExplicitFixedOfficialMetadataAndConstructsTrustedLink()
    {
        using Handler handler = new((message, _) =>
        {
            Assert.Equal(HttpMethod.Get, message.Method);
            Assert.Equal("https://api.github.com/repos/Nexusov/scrcpy-seamless/releases?per_page=30", message.RequestUri!.AbsoluteUri);
            Assert.Equal("application/vnd.github+json", Assert.Single(message.Headers.Accept).MediaType);
            Assert.Equal("2026-03-10", Assert.Single(message.Headers.GetValues("X-GitHub-Api-Version")));
            Assert.NotEmpty(message.Headers.UserAgent);
            Assert.Null(message.Headers.Authorization);
            Assert.False(message.Headers.Contains("Cookie"));
            return Task.FromResult(Json("[{\"tag_name\":\"v2.0.1+build.7\",\"draft\":false,\"prerelease\":false," +
                "\"html_url\":\"https://untrusted.invalid/launch\",\"body\":\"<script>private</script>\"}]"));
        });
        using GitHubReleaseChecker checker = Checker(handler);
        Assert.Equal(0, handler.Calls);

        ReleaseCheckResult result = await checker.CheckAsync(new("2.0.0", ReleaseChannel.Stable), TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.Calls);
        Assert.Equal(ReleaseCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal("2.0.1+build.7", result.Version);
        Assert.Equal("https://github.com/Nexusov/scrcpy-seamless/releases/tag/v2.0.1%2Bbuild.7", result.ReleaseUri!.AbsoluteUri);
        Assert.Equal("github.com", result.ReleaseUri.Host);
    }

    [Theory]
    [InlineData(ReleaseChannel.Stable, "2.0.0")]
    [InlineData(ReleaseChannel.Prerelease, "2.1.0-beta.11")]
    public async Task SelectsSemanticMaximumWithinExplicitChannel(ReleaseChannel channel, string expected)
    {
        string[] tags = ["v2.1.0-beta.2", "v2.0.0", "v2.1.0-beta.11", "v1.9.9", "old-unversioned"];
        string body = JsonSerializer.Serialize(tags.Select(tag => new
        {
            tag_name = tag,
            draft = false,
            prerelease = tag.Contains("beta", StringComparison.Ordinal),
        }));
        using Handler handler = Respond(body);
        using GitHubReleaseChecker checker = Checker(handler);

        ReleaseCheckResult result = await checker.CheckAsync(new("1.0.0", channel), TestContext.Current.CancellationToken);

        Assert.Equal(ReleaseCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal(expected, result.Version);
    }

    [Theory]
    [InlineData("2.0.1+local", "v2.0.1+other")]
    [InlineData("3.0.0-dev.1", "v2.0.1")]
    public async Task DoesNotInventUpdateFromBuildMetadataOrOlderWindow(string current, string tag)
    {
        using Handler handler = Respond($"[{{\"tag_name\":\"{tag}\",\"draft\":false,\"prerelease\":false}}]");
        using GitHubReleaseChecker checker = Checker(handler);
        ReleaseCheckResult result = await checker.CheckAsync(new(current, ReleaseChannel.Prerelease), TestContext.Current.CancellationToken);
        Assert.Equal(ReleaseCheckStatus.NoNewerRelease, result.Status);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[{\"tag_name\":\"v99.0.0\",\"draft\":true,\"prerelease\":false}]")]
    [InlineData("[{\"tag_name\":\"v99.0.0-beta\",\"draft\":false,\"prerelease\":false}]")]
    [InlineData("[{\"tag_name\":\"not-semver\",\"draft\":false,\"prerelease\":false}]")]
    public async Task UnknownTagsDraftsAndExcludedChannelDoNotBecomeAnUpdate(string body)
    {
        using Handler handler = Respond(body);
        using GitHubReleaseChecker checker = Checker(handler);
        Assert.Equal(ReleaseCheckStatus.NoEligibleRelease,
            (await checker.CheckAsync(new("2.0.0", ReleaseChannel.Stable), TestContext.Current.CancellationToken)).Status);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[null]")]
    [InlineData("[{\"tag_name\":\"v2.0.1\",\"draft\":false}]")]
    [InlineData("[{\"tag_name\":42,\"draft\":false,\"prerelease\":false}]")]
    [InlineData("[{\"tag_name\":\"v2.0.1\",\"draft\":\"false\",\"prerelease\":false}]")]
    [InlineData("[{\"tag_name\":\"v2.0.1\",\"draft\":true,\"draft\":false,\"prerelease\":false}]")]
    [InlineData("[{")]
    public async Task RejectsMalformedOrAmbiguousMetadataWithoutRawOutput(string body)
    {
        using Handler handler = Respond(body);
        using GitHubReleaseChecker checker = Checker(handler);
        ReleaseCheckResult result = await checker.CheckAsync(new("2.0.0", ReleaseChannel.Stable), TestContext.Current.CancellationToken);
        Assert.Equal(ReleaseCheckStatus.InvalidResponse, result.Status);
        Assert.Null(result.Version);
        Assert.Null(result.ReleaseUri);
    }

    [Fact]
    public async Task BoundsEntryCountAndJsonDepth()
    {
        string entry = Release[1..^1];
        using Handler many = Respond("[" + string.Join(',', Enumerable.Repeat(entry, GitHubReleaseChecker.MaximumReleases + 1)) + "]");
        using GitHubReleaseChecker manyChecker = Checker(many);
        Assert.Equal(ReleaseCheckStatus.InvalidResponse,
            (await manyChecker.CheckAsync(new("2.0.0", ReleaseChannel.Stable), TestContext.Current.CancellationToken)).Status);
        string deep = "[{\"tag_name\":\"v2.0.1\",\"draft\":false,\"prerelease\":false,\"ignored\":" +
            new string('[', GitHubReleaseChecker.MaximumJsonDepth) + "0" + new string(']', GitHubReleaseChecker.MaximumJsonDepth) + "}]";
        using Handler nested = Respond(deep);
        using GitHubReleaseChecker nestedChecker = Checker(nested);
        Assert.Equal(ReleaseCheckStatus.InvalidResponse,
            (await nestedChecker.CheckAsync(new("2.0.0", ReleaseChannel.Stable), TestContext.Current.CancellationToken)).Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BoundsDeclaredAndUndeclaredBodyAndDisposesStream(bool declared)
    {
        TrackingStream stream = new(new byte[GitHubReleaseChecker.MaximumResponseBytes + 8192]);
        using Handler handler = new((_, _) =>
        {
            StreamContent content = new(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            if (declared)
            {
                content.Headers.ContentLength = GitHubReleaseChecker.MaximumResponseBytes + 8192;
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        using GitHubReleaseChecker checker = Checker(handler);
        Assert.Equal(ReleaseCheckStatus.ResponseTooLarge,
            (await checker.CheckAsync(new("2.0.0", ReleaseChannel.Stable), TestContext.Current.CancellationToken)).Status);
        Assert.True(stream.Disposed);
        Assert.Equal(declared ? 0 : GitHubReleaseChecker.MaximumResponseBytes + 1, stream.BytesRead);
    }

    [Theory]
    [InlineData(429, null, ReleaseCheckStatus.RateLimited)]
    [InlineData(403, "0", ReleaseCheckStatus.RateLimited)]
    [InlineData(403, "12", ReleaseCheckStatus.Unavailable)]
    [InlineData(404, null, ReleaseCheckStatus.Unavailable)]
    [InlineData(500, null, ReleaseCheckStatus.Unavailable)]
    [InlineData(302, null, ReleaseCheckStatus.Unavailable)]
    public async Task ReportsHttpStatesWithoutRetryOrRedirect(int code, string? remaining, ReleaseCheckStatus expected)
    {
        using Handler handler = new((_, _) =>
        {
            HttpResponseMessage response = new((HttpStatusCode)code);
            response.Headers.Location = new Uri("https://untrusted.invalid/");

            if (remaining is not null)
            {
                response.Headers.Add("X-RateLimit-Remaining", remaining);
            }

            return Task.FromResult(response);
        });
        using GitHubReleaseChecker checker = Checker(handler);
        Assert.Equal(expected, (await checker.CheckAsync(new("2.0.0", ReleaseChannel.Stable), TestContext.Current.CancellationToken)).Status);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task SecondaryRateLimitHasTypedStatus()
    {
        using Handler handler = new((_, _) =>
        {
            HttpResponseMessage response = new(HttpStatusCode.Forbidden);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
            return Task.FromResult(response);
        });
        using GitHubReleaseChecker checker = Checker(handler);
        Assert.Equal(ReleaseCheckStatus.RateLimited,
            (await checker.CheckAsync(new("2.0.0", ReleaseChannel.Stable), TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task CancellationAndControlledTimeoutRemainDistinct()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using Handler handler = new(async (_, token) =>
        {
            entered.TrySetResult();
            TaskCompletionSource<HttpResponseMessage> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration = token.Register(() => pending.TrySetCanceled(token));
            return await pending.Task;
        });
        ManualClock clock = new();
        using GitHubReleaseChecker checker = new(handler, clock, Timeout);
        using CancellationTokenSource cancellation = new();
        Task<ReleaseCheckResult> checking = checker.CheckAsync(new("2.0.0", ReleaseChannel.Stable), cancellation.Token);
        await entered.Task.WaitAsync(TestBudget, TestContext.Current.CancellationToken);
        cancellation.Cancel();
        Assert.Equal(ReleaseCheckStatus.Cancelled, (await checking.WaitAsync(TestBudget, TestContext.Current.CancellationToken)).Status);
        Assert.True(clock.Timer!.Disposed);

        entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        checking = checker.CheckAsync(new("2.0.0", ReleaseChannel.Stable), TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TestBudget, TestContext.Current.CancellationToken);
        Assert.Equal(Timeout, clock.Timer!.DueTime);
        clock.Timer.Fire();
        Assert.Equal(ReleaseCheckStatus.TimedOut, (await checking.WaitAsync(TestBudget, TestContext.Current.CancellationToken)).Status);
        Assert.True(clock.Timer.Disposed);
    }

    [Fact]
    public async Task OfflineAndUnattributedCancellationDoNotExposeExceptions()
    {
        using Handler offline = new((_, _) => throw new HttpRequestException("private network details"));
        using GitHubReleaseChecker offlineChecker = Checker(offline);
        ReleaseCheckResult result = await offlineChecker.CheckAsync(new("2.0.0", ReleaseChannel.Stable), TestContext.Current.CancellationToken);
        Assert.Equal(new ReleaseCheckResult(ReleaseCheckStatus.Offline), result);
        using Handler cancelled = new((_, _) => throw new OperationCanceledException("private details"));
        using GitHubReleaseChecker cancelledChecker = Checker(cancelled);
        Assert.Equal(new ReleaseCheckResult(ReleaseCheckStatus.Unavailable),
            await cancelledChecker.CheckAsync(new("2.0.0", ReleaseChannel.Stable), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TimeoutWhileReadingBodySettlesStreamAndTimer()
    {
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        BlockingReadStream stream = new(entered);
        using Handler handler = new((_, _) =>
        {
            StreamContent content = new(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        ManualClock clock = new();
        using GitHubReleaseChecker checker = new(handler, clock, Timeout);
        Task<ReleaseCheckResult> checking = checker.CheckAsync(new("2.0.0", ReleaseChannel.Stable),
            TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TestBudget, TestContext.Current.CancellationToken);
        clock.Timer!.Fire();
        ReleaseCheckResult result = await checking.WaitAsync(TestBudget, TestContext.Current.CancellationToken);
        Assert.Equal(ReleaseCheckStatus.TimedOut, result.Status);
        Assert.True(stream.Disposed);
        Assert.True(clock.Timer.Disposed);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData(null)]
    public async Task MissingOrNonJsonMediaCannotBecomeAnUpdate(string? media)
    {
        using Handler handler = new((_, _) =>
        {
            HttpResponseMessage response = Json(Release);
            response.Content.Headers.ContentType = media is null ? null : new MediaTypeHeaderValue(media);
            return Task.FromResult(response);
        });
        using GitHubReleaseChecker checker = Checker(handler);
        Assert.Equal(ReleaseCheckStatus.InvalidResponse,
            (await checker.CheckAsync(new("2.0.0", ReleaseChannel.Stable), TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task InvalidCurrentVersionOrChannelNeverStartsRequest()
    {
        using Handler handler = Respond(Release);
        using GitHubReleaseChecker checker = Checker(handler);
        Assert.Equal(ReleaseCheckStatus.InvalidRequest,
            (await checker.CheckAsync(new("2.0", ReleaseChannel.Stable), TestContext.Current.CancellationToken)).Status);
        Assert.Equal(ReleaseCheckStatus.InvalidRequest,
            (await checker.CheckAsync(new("2.0.0", (ReleaseChannel)42), TestContext.Current.CancellationToken)).Status);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Equal(ReleaseCheckStatus.Cancelled,
            (await checker.CheckAsync(new("2.0.0", ReleaseChannel.Stable), cancellation.Token)).Status);
        Assert.Equal(0, handler.Calls);
    }

    private static GitHubReleaseChecker Checker(HttpMessageHandler handler) => new(handler, TimeProvider.System, Timeout);

    private static Handler Respond(string body) => new((_, _) => Task.FromResult(Json(body)));

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return send(request, cancellationToken);
        }
    }

    private class TrackingStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream inner = new(bytes);
        public int BytesRead { get; private set; }
        public bool Disposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = inner.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = inner.Read(buffer.Span);
            BytesRead += read;
            return ValueTask.FromResult(read);
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class BlockingReadStream(TaskCompletionSource entered) : TrackingStream([])
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            entered.TrySetResult();
            TaskCompletionSource<int> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration = cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));
            return await pending.Task;
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        public ManualTimer? Timer { get; private set; }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Timer = new(callback, state, dueTime);
            return Timer;
        }
    }

    private sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan dueTime) : ITimer
    {
        public TimeSpan DueTime { get; } = dueTime;
        public bool Disposed { get; private set; }
        public bool Change(TimeSpan dueTime, TimeSpan period) => !Disposed;
        public void Fire() => callback(state);
        public void Dispose() => Disposed = true;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
