using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using GameSync.Core.Storage;
using Google;

namespace GameSync.Storage.Drive;

/// <summary>CLOUD-04: Drive's failures, told apart and retried with backoff where waiting helps.</summary>
internal static class DriveErrors
{
    /// <summary>Runs one Drive call, retrying rate limits, Google's own errors and a dropped connection.</summary>
    public static async Task<T> RetryAsync<T>(Func<Task<T>> call, CancellationToken ct, Func<int, TimeSpan>? backoff = null)
    {
        backoff ??= attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await call();
            }
            catch (Exception e) when (!ct.IsCancellationRequested && Classify(e) is { } cloud)
            {
                var retries = cloud.Kind switch
                {
                    CloudErrorKind.RateLimited => 5,
                    CloudErrorKind.Offline => 1,
                    CloudErrorKind.Other when e is GoogleApiException { HttpStatusCode: >= HttpStatusCode.InternalServerError } => 3,
                    _ => 0,
                };
                if (attempt > retries)
                {
                    throw cloud;
                }

                await Task.Delay(backoff(attempt), ct);
            }
        }
    }

    public static async Task RetryAsync(Func<Task> call, CancellationToken ct) =>
        await RetryAsync(async () =>
        {
            await call();
            return true;
        }, ct);

    /// <summary>The cloud error behind <paramref name="e"/>, or null when it isn't one (a bug, or this PC's own disk).</summary>
    public static CloudException? Classify(Exception e) => e switch
    {
        CloudException cloud => cloud,
        GoogleApiException api => FromApi((int)api.HttpStatusCode, api.Error?.Errors?.FirstOrDefault()?.Reason, api.Error?.Message ?? api.Message, api),
        HttpRequestException or HttpIOException or TaskCanceledException or TimeoutException =>
            new CloudException(CloudErrorKind.Offline, "You're offline, or Google Drive can't be reached.", e),
        IOException { InnerException: SocketException } => new CloudException(CloudErrorKind.Offline, "You're offline, or Google Drive can't be reached.", e),
        _ => null,
    };

    /// <summary>What an HTTP status and Google's error reason mean for the user.</summary>
    public static CloudException FromApi(int status, string? reason, string message, Exception? inner = null) => (status, reason) switch
    {
        (401, _) => new CloudException(CloudErrorKind.SignInExpired, "The sign-in to Google Drive expired.", inner),
        (403, "storageQuotaExceeded" or "quotaExceeded") => new CloudException(CloudErrorKind.StorageFull, "Google Drive is full.", inner),
        (429, _) or (403, "userRateLimitExceeded" or "rateLimitExceeded" or "sharingRateLimitExceeded") =>
            new CloudException(CloudErrorKind.RateLimited, "Google Drive asked GameSync to slow down.", inner),
        (403, "cannotDownloadAbusiveFile") =>
            new CloudException(CloudErrorKind.Flagged, "Google flagged this file as malware, so GameSync never downloads it.", inner),
        (>= 500, _) => new CloudException(CloudErrorKind.Other, $"Google Drive had a problem ({status}); it's tried again next sync.", inner),
        _ => new CloudException(CloudErrorKind.Other, $"Google Drive refused the request: {message}", inner),
    };
}
