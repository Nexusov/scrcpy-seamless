using System.IO.Pipes;
using System.Text;
using ScrcpySeamless.Core;
using ScrcpySeamless.Core.Application.Activation;
using ScrcpySeamless.Infrastructure.Activation;
using Xunit;

namespace ScrcpySeamless.Infrastructure.Tests.Activation;

/// <summary>Protects same-user ownership and the restricted activation channel.</summary>
public sealed class WindowsControlCenterActivationTests
{
    private static readonly TimeSpan TestDeadline = TimeSpan.FromSeconds(10);

    /// <summary>A repeated launch forwards intent without acquiring another owner.</summary>
    [Fact]
    public async Task SameScopeForwardsAndReleasesOwnership()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = NewSyntheticRoot();
        await using WindowsControlCenterActivation primary = new(root);
        await using WindowsControlCenterActivation repeated = new(root.ToUpperInvariant());
        ActivationRequest show = new(ActivationKind.ShowControlCenter);
        Assert.Equal(ActivationDisposition.PrimaryOwner,
            await primary.AcquireOrForwardAsync(show, TestContext.Current.CancellationToken));

        await using IAsyncEnumerator<ActivationRequest> observed =
            primary.ObserveRequestsAsync(TestContext.Current.CancellationToken)
                .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Task<bool> next = observed.MoveNextAsync().AsTask();
        Assert.Equal(ActivationDisposition.ForwardedToPrimary,
            await repeated.AcquireOrForwardAsync(show, TestContext.Current.CancellationToken));
        Assert.True(await next.WaitAsync(TestDeadline, TestContext.Current.CancellationToken));
        Assert.Equal(ActivationKind.ShowControlCenter, observed.Current.Kind);
        Assert.Null(observed.Current.SessionId);
        Assert.False(Directory.Exists(root));

        await primary.DisposeAsync();
        await using WindowsControlCenterActivation successor = new(root);
        Assert.Equal(ActivationDisposition.PrimaryOwner,
            await successor.AcquireOrForwardAsync(show, TestContext.Current.CancellationToken));
    }

    /// <summary>Distinct DEV data roots cannot activate each other's control centers.</summary>
    [Fact]
    public async Task SeparateDataRootsOwnSeparateEndpoints()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = NewSyntheticRoot();
        await using WindowsControlCenterActivation first = new(Path.Combine(root, "one"));
        await using WindowsControlCenterActivation second = new(Path.Combine(root, "two"));
        ActivationRequest show = new(ActivationKind.ShowControlCenter);

        Assert.Equal(ActivationDisposition.PrimaryOwner,
            await first.AcquireOrForwardAsync(show, TestContext.Current.CancellationToken));
        Assert.Equal(ActivationDisposition.PrimaryOwner,
            await second.AcquireOrForwardAsync(show, TestContext.Current.CancellationToken));
        Assert.NotEqual(first.PipeName, second.PipeName);
        Assert.False(Directory.Exists(root));
    }

    /// <summary>Malformed and oversized messages cannot become activation commands.</summary>
    [Fact]
    public async Task RejectsUntrustedCommandBeforeDeliveringValidSessionFocus()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = NewSyntheticRoot();
        await using WindowsControlCenterActivation primary = new(root);
        await using WindowsControlCenterActivation repeated = new(root);
        ActivationRequest show = new(ActivationKind.ShowControlCenter);
        Assert.Equal(ActivationDisposition.PrimaryOwner,
            await primary.AcquireOrForwardAsync(show, TestContext.Current.CancellationToken));
        using CancellationTokenSource observationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await using IAsyncEnumerator<ActivationRequest> observed =
            primary.ObserveRequestsAsync(observationCancellation.Token)
                .GetAsyncEnumerator(observationCancellation.Token);
        Task<bool> next = observed.MoveNextAsync().AsTask();

        try
        {
            Assert.Equal("NO", await SendRawCommandAsync(primary.PipeName, "FOCUS:00000000-0000-0000-0000-000000000000\n",
                cancellationToken: TestContext.Current.CancellationToken));
            try
            {
                Assert.Equal("NO", await SendRawCommandAsync(primary.PipeName, new string('X', 65) + "\n",
                    cancellationToken: TestContext.Current.CancellationToken));
            }
            catch (IOException)
            {
                // The bounded server may close before an oversized peer finishes writing.
            }
            Assert.False(next.IsCompleted);

            SessionId sessionId = SessionId.New();
            Assert.Equal(ActivationDisposition.ForwardedToPrimary,
                await repeated.AcquireOrForwardAsync(
                    new ActivationRequest(ActivationKind.FocusSession, sessionId),
                    TestContext.Current.CancellationToken));
            Assert.True(await next.WaitAsync(TestDeadline, TestContext.Current.CancellationToken));
            Assert.Equal(ActivationKind.FocusSession, observed.Current.Kind);
            Assert.Equal(sessionId, observed.Current.SessionId);

            // Complete the real channel to prove neither invalid peer nor a duplicate was delivered.
            await primary.DisposeAsync();
            next = observed.MoveNextAsync().AsTask();
            Assert.False(await next.WaitAsync(TestDeadline, TestContext.Current.CancellationToken));
        }
        finally
        {
            observationCancellation.Cancel();

            try
            {
                await next;
            }
            catch (OperationCanceledException) when (observationCancellation.IsCancellationRequested)
            {
                // Settle MoveNext before disposing its async enumerator on any assertion path.
            }
        }
    }

    /// <summary>Disposal cancels a connected silent peer and releases the owner reservation.</summary>
    [Fact]
    public async Task DisposeDoesNotWaitForSilentPeer()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string root = NewSyntheticRoot();
        WindowsControlCenterActivation primary = new(root);
        ActivationRequest show = new(ActivationKind.ShowControlCenter);
        Assert.Equal(ActivationDisposition.PrimaryOwner,
            await primary.AcquireOrForwardAsync(show, TestContext.Current.CancellationToken));
        await using NamedPipeClientStream silentPeer =
            new(".", primary.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await silentPeer.ConnectAsync(TestContext.Current.CancellationToken);

        await primary.DisposeAsync().AsTask().WaitAsync(TestDeadline, TestContext.Current.CancellationToken);
        await using WindowsControlCenterActivation successor = new(root);
        Assert.Equal(ActivationDisposition.PrimaryOwner,
            await successor.AcquireOrForwardAsync(show, TestContext.Current.CancellationToken));
    }

    /// <summary>Only fully qualified roots can reserve a normal activation scope.</summary>
    [Fact]
    public void RejectsRelativeScope()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Throws<ArgumentException>(() => new WindowsControlCenterActivation("relative-data"));
    }

    private static string NewSyntheticRoot() =>
        Path.Combine(Path.GetTempPath(), "scrcpy-activation-test-" + Guid.NewGuid().ToString("N"));

    /// <summary>Publishes a bounded raw peer independently of the caller's context and owns cancellation through disposal.</summary>
    internal static async Task<string> SendRawCommandAsync(string pipeName, string command,
        Action<string>? observe = null, CancellationToken cancellationToken = default)
    {
        await using NamedPipeClientStream client = new(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, TestContext.Current.CancellationToken);
        deadline.CancelAfter(TestDeadline);
        observe?.Invoke("ConnectStarted");
        // Fixture transport progress must not wait for the caller's synchronization context.
        await client.ConnectAsync(deadline.Token).ConfigureAwait(false);
        observe?.Invoke("Connected");
        observe?.Invoke("WriteStarted");
        await client.WriteAsync(Encoding.ASCII.GetBytes(command), deadline.Token).ConfigureAwait(false);
        observe?.Invoke("Written");
        await client.FlushAsync(deadline.Token).ConfigureAwait(false);
        observe?.Invoke("Flushed");
        byte[] response = new byte[3];
        await client.ReadExactlyAsync(response, deadline.Token).ConfigureAwait(false);
        observe?.Invoke("ResponseRead");
        return Encoding.ASCII.GetString(response).TrimEnd('\n');
    }
}
