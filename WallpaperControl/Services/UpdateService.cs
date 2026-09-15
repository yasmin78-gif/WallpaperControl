using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace WallpaperControl
{
    internal enum UpdateCheckStatus
    {
        UpToDate,
        UpdateAvailable,
        Failed
    }

    internal sealed record UpdateCheckResult(
        UpdateCheckStatus Status,
        Version CurrentVersion,
        Version? LatestVersion,
        Uri? ReleaseUri);

    /// <summary>
    /// Performs a read-only check against WallpaperControl's latest stable
    /// GitHub release. It never downloads or installs application updates.
    /// </summary>
    internal sealed class UpdateService : IDisposable
    {
        private static readonly Uri LatestReleaseUri =
            new("https://github.com/yasmin78-gif/WallpaperControl/releases/latest");

        private readonly HttpClient httpClient;
        private readonly bool ownsHttpClient;

        public UpdateService()
        {
            HttpClientHandler handler = new()
            {
                AllowAutoRedirect = true,
                AutomaticDecompression =
                    DecompressionMethods.GZip |
                    DecompressionMethods.Deflate |
                    DecompressionMethods.Brotli
            };

            httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "WallpaperControl-UpdateCheck");

            ownsHttpClient = true;
        }

        internal UpdateService(HttpClient httpClient)
        {
            this.httpClient =
                httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            ownsHttpClient = false;
        }

        public async Task<UpdateCheckResult> CheckAsync(
            CancellationToken cancellationToken = default)
        {
            Version currentVersion = GetCurrentVersion();

            try
            {
                using HttpRequestMessage request =
                    new(HttpMethod.Get, LatestReleaseUri);

                using HttpResponseMessage response =
                    await httpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken)
                    .ConfigureAwait(false);

                response.EnsureSuccessStatusCode();

                Uri? finalUri = response.RequestMessage?.RequestUri;
                Version? latestVersion = TryGetVersionFromReleaseUri(finalUri);

                if (latestVersion == null)
                {
                    return new UpdateCheckResult(
                        UpdateCheckStatus.Failed,
                        currentVersion,
                        null,
                        finalUri);
                }

                UpdateCheckStatus status =
                    latestVersion > currentVersion
                        ? UpdateCheckStatus.UpdateAvailable
                        : UpdateCheckStatus.UpToDate;

                return new UpdateCheckResult(
                    status,
                    currentVersion,
                    latestVersion,
                    finalUri);
            }
            catch (OperationCanceledException ex)
                when (!cancellationToken.IsCancellationRequested)
            {
                AppLogger.Warning("Update check timed out.", ex);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (HttpRequestException ex)
            {
                AppLogger.Warning("Update check failed.", ex);
            }
            catch (Exception ex)
            {
                AppLogger.Warning("Update check failed.", ex);
            }

            return new UpdateCheckResult(
                UpdateCheckStatus.Failed,
                currentVersion,
                null,
                null);
        }

        internal static Version GetCurrentVersion()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();

            string? informationalVersion =
                assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion;

            Version? parsed = ParseVersion(informationalVersion);
            if (parsed != null)
                return parsed;

            return assembly.GetName().Version ?? new Version(0, 0, 0);
        }

        internal static Version? TryGetVersionFromReleaseUri(Uri? releaseUri)
        {
            if (releaseUri == null)
                return null;

            const string marker = "/releases/tag/";
            string path = releaseUri.AbsolutePath;
            int markerIndex = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

            if (markerIndex < 0)
                return null;

            string tag = Uri.UnescapeDataString(
                path[(markerIndex + marker.Length)..]).Trim('/');

            return ParseVersion(tag);
        }

        internal static Version? ParseVersion(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            string normalized = value.Trim();

            if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                normalized = normalized[1..];

            int metadataIndex = normalized.IndexOf('+');
            if (metadataIndex >= 0)
                normalized = normalized[..metadataIndex];

            int prereleaseIndex = normalized.IndexOf('-');
            if (prereleaseIndex >= 0)
                normalized = normalized[..prereleaseIndex];

            return Version.TryParse(normalized, out Version? version)
                ? version
                : null;
        }

        public void Dispose()
        {
            if (ownsHttpClient)
                httpClient.Dispose();
        }
    }
}
