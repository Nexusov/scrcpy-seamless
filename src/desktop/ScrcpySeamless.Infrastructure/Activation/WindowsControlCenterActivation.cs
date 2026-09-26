using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading.Channels;
using ScrcpySeamless.Core.Application.Activation;

namespace ScrcpySeamless.Infrastructure.Activation;

/// <summary>Coordinates one normal control center per Windows user and data root.</summary>
public sealed class WindowsControlCenterActivation : IControlCenterActivation
{
    private const int ConnectionTimeoutMilliseconds = 3000;
    private const int RequestTimeoutMilliseconds = 5000;
    private const int MaximumRequestBytes = 64;
    private readonly SemaphoreSlim acquisition = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Channel<ActivationRequest> requests = Channel.CreateBounded<ActivationRequest>(
        new BoundedChannelOptions(16) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    private OwnerLease? owner;
    private Task? listener;
    private NamedPipeServerStream? activeServer;
    private bool disposed;

    internal string PipeName { get; }

    /// <summary>Uses the explicit normal data root as the scope of the activation endpoint.</summary>
    public WindowsControlCenterActivation(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Control-center activation requires Windows.");
        }

        if (!Path.IsPathFullyQualified(dataRoot))
        {
            throw new ArgumentException("The data root must be absolute.", nameof(dataRoot));
        }

        string normalizedRoot = Path.GetFullPath(dataRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string userId = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("The current Windows user identity is unavailable.");
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(userId + "\n" + normalizedRoot.ToUpperInvariant()));
        string scope = Convert.ToHexString(digest);
        PipeName = "scrcpy-seamless-control-" + scope;
    }

    /// <summary>Starts the sole owner listener or sends a bounded activation command to it.</summary>
    public async Task<ActivationDisposition> AcquireOrForwardAsync(
        ActivationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await acquisition.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            if (owner is not null)
            {
                return ActivationDisposition.PrimaryOwner;
            }

            OwnerLease candidate = await OwnerLease.TryAcquireAsync("Local\\" + PipeName, cancellationToken)
                .ConfigureAwait(false);

            if (candidate.IsOwner)
            {
                NamedPipeServerStream server;

                try
                {
                    server = CreateServer();
                }
                catch
                {
                    candidate.Dispose();
                    throw;
                }

                owner = candidate;
                activeServer = server;
                listener = ListenAsync(server, lifetime.Token);
                return ActivationDisposition.PrimaryOwner;
            }

            candidate.Dispose();
            await ForwardAsync(request, cancellationToken).ConfigureAwait(false);
            return ActivationDisposition.ForwardedToPrimary;
        }
        finally
        {
            acquisition.Release();
        }
    }

    /// <summary>Returns only validated commands received by the primary owner.</summary>
    public IAsyncEnumerable<ActivationRequest> ObserveRequestsAsync(CancellationToken cancellationToken)
    {
        if (owner is null)
        {
            throw new InvalidOperationException("Only the primary owner can observe activation requests.");
        }

        return requests.Reader.ReadAllAsync(cancellationToken);
    }

    /// <summary>Releases the listener and the exact process-owned reservation.</summary>
    public async ValueTask DisposeAsync()
    {
        await acquisition.WaitAsync().ConfigureAwait(false);

        try
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            lifetime.Cancel();
            activeServer?.Dispose();

            if (listener is not null)
            {
                await listener.ConfigureAwait(false);
            }

            requests.Writer.TryComplete();
            owner?.Dispose();
            lifetime.Dispose();
        }
        finally
        {
            acquisition.Release();
        }
    }

    private NamedPipeServerStream CreateServer() => new(
        PipeName,
        PipeDirection.InOut,
        1,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task ListenAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using (server)
                {
                    await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                    using CancellationTokenSource requestDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    requestDeadline.CancelAfter(RequestTimeoutMilliseconds);

                    try
                    {
                        ActivationRequest? request = await ReadRequestAsync(server, requestDeadline.Token)
                            .ConfigureAwait(false);
                        bool accepted = request is not null;

                        if (request is not null)
                        {
                            await requests.Writer.WriteAsync(request, requestDeadline.Token).ConfigureAwait(false);
                        }

                        await server.WriteAsync(Encoding.ASCII.GetBytes(accepted ? "OK\n" : "NO\n"), requestDeadline.Token)
                            .ConfigureAwait(false);
                        await server.FlushAsync(requestDeadline.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        // A silent peer cannot hold the single activation listener indefinitely.
                    }
                    catch (IOException) when (!cancellationToken.IsCancellationRequested)
                    {
                        // A disconnected peer does not change primary ownership.
                    }
                }

                server = CreateServer();
                activeServer = server;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            server.Dispose();
        }
    }

    private async Task ForwardAsync(ActivationRequest request, CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(RequestTimeoutMilliseconds);
        await using NamedPipeClientStream client = new(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(ConnectionTimeoutMilliseconds, deadline.Token).ConfigureAwait(false);
        string command = request.Kind switch
        {
            ActivationKind.ShowControlCenter => "SHOW\n",
            ActivationKind.FocusSession => "FOCUS:" + request.SessionId!.Value.Value.ToString("D") + "\n",
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };
        await client.WriteAsync(Encoding.ASCII.GetBytes(command), deadline.Token).ConfigureAwait(false);
        await client.FlushAsync(deadline.Token).ConfigureAwait(false);
        string? response = await ReadLineAsync(client, 3, deadline.Token).ConfigureAwait(false);

        if (response != "OK")
        {
            throw new IOException("The existing control center did not accept the activation request.");
        }
    }

    private static async Task<ActivationRequest?> ReadRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        string? command = await ReadLineAsync(stream, MaximumRequestBytes, cancellationToken).ConfigureAwait(false);

        if (command == "SHOW")
        {
            return new ActivationRequest(ActivationKind.ShowControlCenter);
        }

        if (command is not null && command.StartsWith("FOCUS:", StringComparison.Ordinal) &&
            Guid.TryParseExact(command["FOCUS:".Length..], "D", out Guid sessionId) && sessionId != Guid.Empty)
        {
            return new ActivationRequest(ActivationKind.FocusSession, new Core.SessionId(sessionId));
        }

        return null;
    }

    private static async Task<string?> ReadLineAsync(Stream stream, int maximumBytes, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[maximumBytes];
        byte[] oneByte = new byte[1];
        int length = 0;

        while (length < maximumBytes)
        {
            int count = await stream.ReadAsync(oneByte, cancellationToken).ConfigureAwait(false);

            if (count == 0)
            {
                return null;
            }

            if (oneByte[0] == (byte)'\n')
            {
                return Encoding.ASCII.GetString(buffer, 0, length);
            }

            if (oneByte[0] < 0x20 || oneByte[0] > 0x7E)
            {
                return null;
            }

            buffer[length++] = oneByte[0];
        }

        return null;
    }

    private sealed class OwnerLease : IDisposable
    {
        private readonly ManualResetEventSlim release = new();
        private readonly Thread thread;
        private bool disposed;

        public bool IsOwner { get; private set; }

        private OwnerLease(string name, TaskCompletionSource<bool> acquired)
        {
            thread = new Thread(() => OwnMutex(name, acquired)) { IsBackground = true, Name = "Control-center activation owner" };
            thread.Start();
        }

        public static async Task<OwnerLease> TryAcquireAsync(string name, CancellationToken cancellationToken)
        {
            TaskCompletionSource<bool> acquired = new(TaskCreationOptions.RunContinuationsAsynchronously);
            OwnerLease lease = new(name, acquired);

            try
            {
                lease.IsOwner = await acquired.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                return lease;
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            release.Set();
            thread.Join();
            release.Dispose();
        }

        private void OwnMutex(string name, TaskCompletionSource<bool> acquired)
        {
            try
            {
                using Mutex mutex = new(false, name);
                bool hasOwnership;

                try
                {
                    hasOwnership = mutex.WaitOne(0);
                }
                catch (AbandonedMutexException)
                {
                    hasOwnership = true;
                }

                acquired.TrySetResult(hasOwnership);

                if (hasOwnership)
                {
                    release.Wait();
                    mutex.ReleaseMutex();
                }
            }
            catch (Exception exception)
            {
                acquired.TrySetException(exception);
            }
        }
    }
}
