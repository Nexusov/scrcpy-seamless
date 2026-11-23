using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ScrcpySeamless.Core.Application.Updates;

namespace ScrcpySeamless.Infrastructure.Updates;

/// <summary>Explicit bounded metadata check, never an installer or background service.</summary>
public sealed class GitHubReleaseChecker : IReleaseChecker, IDisposable
{
    public const int MaximumResponseBytes = 256 * 1024;
    public const int MaximumReleases = 30;
    public const int MaximumJsonDepth = 32;
    private const int ReadBufferSize = 8192;
    private const string ApiVersion = "2026-03-10";
    private const string ReleasePrefix = "https://github.com/Nexusov/scrcpy-seamless/releases/tag/";
    private static readonly Uri Endpoint = new("https://api.github.com/repos/Nexusov/scrcpy-seamless/releases?per_page=30");
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private readonly HttpClient client;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan timeout;
    private bool disposed;

    public GitHubReleaseChecker() : this(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
    }, TimeProvider.System, RequestTimeout)
    {
    }

    internal GitHubReleaseChecker(HttpMessageHandler handler, TimeProvider timeProvider, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        this.timeProvider = timeProvider;
        this.timeout = timeout;
    }

    public async Task<ReleaseCheckResult> CheckAsync(ReleaseCheckRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (cancellationToken.IsCancellationRequested)
        {
            return new(ReleaseCheckStatus.Cancelled);
        }

        if (!Enum.IsDefined(request.Channel) || !SemanticVersion.TryParse(request.CurrentVersion, out SemanticVersion? current))
        {
            return new(ReleaseCheckStatus.InvalidRequest);
        }

        if (disposed)
        {
            return new(ReleaseCheckStatus.Unavailable);
        }

        using CancellationTokenSource deadline = new(timeout, timeProvider);
        using CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        try
        {
            using HttpRequestMessage message = new(HttpMethod.Get, Endpoint);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            message.Headers.UserAgent.ParseAdd("scrcpy-Seamless-release-check/1.0");
            message.Headers.Add("X-GitHub-Api-Version", ApiVersion);
            using HttpResponseMessage response = await client.SendAsync(message,
                HttpCompletionOption.ResponseHeadersRead, lifetime.Token).ConfigureAwait(false);

            if (IsRateLimited(response))
            {
                return new(ReleaseCheckStatus.RateLimited);
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return new(ReleaseCheckStatus.Unavailable);
            }

            string? media = response.Content.Headers.ContentType?.MediaType;

            if (media is not ("application/json" or "application/vnd.github+json"))
            {
                return new(ReleaseCheckStatus.InvalidResponse);
            }

            if (response.Content.Headers.ContentLength > MaximumResponseBytes)
            {
                return new(ReleaseCheckStatus.ResponseTooLarge);
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(lifetime.Token).ConfigureAwait(false);
            byte[] buffer = new byte[MaximumResponseBytes + 1];
            int count = 0;

            while (count < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(count, Math.Min(ReadBufferSize, buffer.Length - count)),
                    lifetime.Token).ConfigureAwait(false);

                if (read == 0)
                {
                    return Parse(buffer.AsMemory(0, count), current!, request.Channel);
                }

                count += read;
            }

            return new(ReleaseCheckStatus.ResponseTooLarge);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(ReleaseCheckStatus.Cancelled);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            return new(ReleaseCheckStatus.TimedOut);
        }
        catch (OperationCanceledException)
        {
            return new(ReleaseCheckStatus.Unavailable);
        }
        catch (HttpRequestException)
        {
            return new(ReleaseCheckStatus.Offline);
        }
        catch (IOException)
        {
            return new(ReleaseCheckStatus.Offline);
        }
        catch (ObjectDisposedException)
        {
            return new(ReleaseCheckStatus.Unavailable);
        }
        catch (JsonException)
        {
            return new(ReleaseCheckStatus.InvalidResponse);
        }
    }

    private static bool IsRateLimited(HttpResponseMessage response) => response.StatusCode == HttpStatusCode.TooManyRequests ||
        response.StatusCode == HttpStatusCode.Forbidden &&
        (response.Headers.RetryAfter is not null ||
         response.Headers.TryGetValues("X-RateLimit-Remaining", out IEnumerable<string>? values) && values.Any(value => value.Trim() == "0"));

    private static ReleaseCheckResult Parse(ReadOnlyMemory<byte> bytes, SemanticVersion current, ReleaseChannel channel)
    {
        using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = MaximumJsonDepth });
        JsonElement root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() > MaximumReleases)
        {
            return new(ReleaseCheckStatus.InvalidResponse);
        }

        SemanticVersion? latest = null;
        string? latestTag = null;

        foreach (JsonElement entry in root.EnumerateArray())
        {
            if (!ReadMetadata(entry, out string? tag, out bool draft, out bool prerelease))
            {
                return new(ReleaseCheckStatus.InvalidResponse);
            }

            string value = tag!.StartsWith('v') ? tag[1..] : tag;

            if (draft || !SemanticVersion.TryParse(value, out SemanticVersion? version) ||
                !ReleaseChannelPolicy.Includes(version!, prerelease, channel))
            {
                continue;
            }

            if (latest is null || version!.CompareTo(latest) > 0)
            {
                latest = version;
                latestTag = tag;
            }
        }

        if (latest is null)
        {
            return new(ReleaseCheckStatus.NoEligibleRelease);
        }

        Uri releaseUri = new(ReleasePrefix + Uri.EscapeDataString(latestTag!));
        return new(latest.CompareTo(current) > 0 ? ReleaseCheckStatus.UpdateAvailable : ReleaseCheckStatus.NoNewerRelease,
            latest.Value, releaseUri);
    }

    private static bool ReadMetadata(JsonElement entry, out string? tag, out bool draft, out bool prerelease)
    {
        tag = null;
        draft = false;
        prerelease = false;

        if (entry.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        HashSet<string> seen = [];

        foreach (JsonProperty property in entry.EnumerateObject())
        {
            if (property.Name is not ("tag_name" or "draft" or "prerelease"))
            {
                continue;
            }

            if (!seen.Add(property.Name))
            {
                return false;
            }

            if (property.Name == "tag_name")
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                tag = property.Value.GetString();

                if (tag is null || tag.Length is 0 or > SemanticVersion.MaximumLength + 1)
                {
                    return false;
                }
            }
            else
            {
                if (property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    return false;
                }

                if (property.Name == "draft")
                {
                    draft = property.Value.GetBoolean();
                }
                else
                {
                    prerelease = property.Value.GetBoolean();
                }
            }
        }

        return seen.Count == 3;
    }

    public void Dispose()
    {
        disposed = true;
        client.Dispose();
    }
}
