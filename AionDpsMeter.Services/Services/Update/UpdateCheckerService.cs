using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;

namespace AionDpsMeter.Services.Services.Update
{
    public sealed class UpdateCheckerService
    {

        public ReleaseInfo? LatestReleaseInfo { get; private set; }

        public const string DisableArgument = "--no-update";

        /// <summary>False when built with -p:DisableUpdater=true or launched with --no-update.</summary>
#if DISABLE_UPDATER
        public static bool IsEnabled => false;
#else
        public static bool IsEnabled { get; } = !Environment.GetCommandLineArgs()
            .Any(a => a.Equals(DisableArgument, StringComparison.OrdinalIgnoreCase));
#endif

        /// <summary>"owner/repo" whose GitHub releases are checked; set by the UpdateRepository MSBuild property.</summary>
        public static string Repository { get; } = typeof(UpdateCheckerService).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "UpdateRepository")?.Value ?? string.Empty;

        private static string ReleasesApiUrl => $"https://api.github.com/repos/{Repository}/releases/latest";

        private const string DownloadFileName = "pending-update.zip";

        private static readonly HttpClient _httpClient = new()
        {
            DefaultRequestHeaders =
            {
                { "User-Agent", "AionDpsMeter-UpdateChecker" }
            },
            Timeout = TimeSpan.FromSeconds(30)
        };

        /// <summary>
        /// Asks GitHub for the latest release of <see cref="Repository"/>. Only run when the user
        /// presses "Check for updates"; a newer release is only reported as available when it
        /// ships a zip with a matching .sig asset.
        /// </summary>
        public async Task<UpdateCheckResult> CheckForUpdateAsync()
        {
            if (!IsEnabled || string.IsNullOrEmpty(Repository))
                return new UpdateCheckResult(UpdateCheckStatus.Disabled, "Updates are turned off in this build.");

            GithubReleaseDto? dto;
            try
            {
                dto = await _httpClient.GetFromJsonAsync<GithubReleaseDto>(ReleasesApiUrl);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate, "No releases have been published yet.");
            }
            catch (Exception)
            {
                return new UpdateCheckResult(UpdateCheckStatus.Failed, "Couldn't reach GitHub. Check your connection and try again.");
            }

            if (dto is null)
                return new UpdateCheckResult(UpdateCheckStatus.Failed, "GitHub returned an empty response.");

            var latestVersion  = ParseVersion(dto.TagName);
            var currentVersion = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0, 0);

            var latestCompare  = new Version(latestVersion.Major,  latestVersion.Minor,  latestVersion.Build);
            var currentCompare = new Version(currentVersion.Major, currentVersion.Minor, currentVersion.Build);

            if (latestCompare <= currentCompare)
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate, $"You're on the latest version (v{currentCompare}).");

            // Unsigned releases are never offered; there is no source-zip fallback.
            var zip = dto.Assets?.FirstOrDefault(a =>
                a.Name?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true);
            var sig = zip is null ? null : dto.Assets?.FirstOrDefault(a =>
                string.Equals(a.Name, zip.Name + UpdateSigning.SignatureExtension, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(zip?.BrowserDownloadUrl) || string.IsNullOrEmpty(sig?.BrowserDownloadUrl))
                return new UpdateCheckResult(UpdateCheckStatus.Failed, $"{dto.TagName} is out, but it isn't signed, so it won't be installed.");

            LatestReleaseInfo = new ReleaseInfo
            {
                TagName      = dto.TagName ?? string.Empty,
                Name         = dto.Name    ?? dto.TagName ?? string.Empty,
                Body         = dto.Body    ?? string.Empty,
                HtmlUrl      = dto.HtmlUrl ?? string.Empty,
                ZipUrl       = zip.BrowserDownloadUrl,
                SignatureUrl = sig.BrowserDownloadUrl
            };
            return new UpdateCheckResult(UpdateCheckStatus.Available, $"{LatestReleaseInfo.Name} is available.", LatestReleaseInfo);
        }

        /// <summary>
        /// Downloads the release zip and its signature next to the executable, verifies the signature
        /// against <see cref="UpdateSigning.PublicKey"/> and returns the zip path.
        /// </summary>
        public async Task<string> DownloadReleaseAsync(ReleaseInfo release, IProgress<int>? progress = null, CancellationToken ct = default)
        {
            if (!IsEnabled)
                throw new InvalidOperationException("Updater is disabled.");

            var exeDir  = AppContext.BaseDirectory;
            var zipPath = Path.Combine(exeDir, DownloadFileName);
            var sigPath = zipPath + UpdateSigning.SignatureExtension;

            var signature = await _httpClient.GetByteArrayAsync(release.SignatureUrl, ct);

            using (var response = await _httpClient.GetAsync(release.ZipUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();

                var total   = response.Content.Headers.ContentLength ?? -1L;
                var written = 0L;

                await using var src  = await response.Content.ReadAsStreamAsync(ct);
                await using var dest = File.Create(zipPath);

                var buffer = new byte[81920];
                int read;
                while ((read = await src.ReadAsync(buffer, ct)) > 0)
                {
                    await dest.WriteAsync(buffer.AsMemory(0, read), ct);
                    written += read;
                    if (total > 0)
                        progress?.Report((int)(written * 100 / total));
                }
            }

            if (!UpdateSigning.Verify(zipPath, release.TagName, signature))
            {
                File.Delete(zipPath);
                throw new InvalidDataException("Update rejected: the signature does not match. The download may have been tampered with.");
            }

            await File.WriteAllBytesAsync(sigPath, signature, ct);
            progress?.Report(100);
            return zipPath;
        }

        private static Version ParseVersion(string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return new Version(0, 0, 0);
            var clean = tag.TrimStart('v', 'V');
            return Version.TryParse(clean, out var v) ? v : new Version(0, 0, 0);
        }

        private sealed class GithubReleaseDto
        {
            [JsonPropertyName("tag_name")]    public string?         TagName    { get; init; }
            [JsonPropertyName("name")]        public string?         Name       { get; init; }
            [JsonPropertyName("body")]        public string?         Body       { get; init; }
            [JsonPropertyName("html_url")]    public string?         HtmlUrl    { get; init; }
            [JsonPropertyName("assets")]      public List<AssetDto>? Assets     { get; init; }
        }

        private sealed class AssetDto
        {
            [JsonPropertyName("name")]                 public string? Name                { get; init; }
            [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; init; }
        }
    }
}
