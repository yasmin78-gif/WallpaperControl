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

    /// <summary>
    /// Describes a release check without downloading or installing an update.
    /// </summary>
    /// <param name="Status">Whether the application is current, an update exists, or the check failed.</param>
    /// <param name="CurrentVersion">The running application version.</param>
    /// <param name="LatestVersion">The discovered release version, or null when unavailable.</param>
    /// <param name="ReleaseUri">The release page address, or null when unavailable.</param>
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

        /// <summary>
        /// Configures release checks with an owned default HTTP client or an injected client.
        /// </summary>
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

        /// <summary>
        /// Configures release checks with an owned default HTTP client or an injected client.
        /// </summary>
        /// <param name="httpClient">The HTTP client to use; the caller retains ownership.</param>
        internal UpdateService(HttpClient httpClient)
        {
            this.httpClient =
                httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            ownsHttpClient = false;
        }

        /// <summary>
        /// Compares the running version with the latest release and reports request or parsing failures as a result.
        /// </summary>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        /// <returns>A task whose result reports whether an update is available, the application is current, or the check failed.</returns>
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

        /// <summary>
        /// Reads the running application&apos;s version from assembly metadata with supported fallbacks.
        /// </summary>
        /// <returns>The running application&apos;s normalized version.</returns>
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

        /// <summary>
        /// Extracts a release version from the final release URL when its format is recognized.
        /// </summary>
        /// <param name="releaseUri">The final release URL after HTTP redirection.</param>
        /// <returns>The release version, or null when the URL is not recognized.</returns>
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

        /// <summary>
        /// Parses a release or assembly version after normalizing supported prefixes and suffixes.
        /// </summary>
        /// <param name="value">The version or release-tag text to parse.</param>
        /// <returns>The parsed version, or null when the input does not contain a valid version.</returns>
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

        /// <summary>
        /// Releases the HTTP client only when it was created by this service.
        /// </summary>
        public void Dispose()
        {
            if (ownsHttpClient)
                httpClient.Dispose();
        }
    }
}
