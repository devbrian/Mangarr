using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.Update
{
    // Sonarr divergence (Phase 29 D-01..D-04 / DIST2-03): replaces NoOpUpdatePackageProvider
    // (Phase 15 D-21 placeholder). GitHub Releases API is the v1.2+ broker. Banner-only —
    // no auto-update mechanism (ROADMAP cross-cutting locks this — the broker informs the
    // user that a newer release exists; the user pulls the new image via `docker pull`.
    // No auto-download / auto-extract / auto-restart path).
    //
    // D-02: Polling cadence reuses the existing TaskManager.ApplicationUpdateCheckCommand
    // schedule (6h). No new scheduled task.
    //
    // D-03: 4-part BuildInfo.Version + 4-part tag_name both stripped to 3-part semver
    // before comparison (1.1.0.42 == 1.1.0.99 ⇒ same release line). Bumping CI run_number
    // does NOT spam the banner.
    //
    // T-29-02-01 mitigation: tag_name is validated against a strict version regex before
    // being parsed into System.Version. Hostile upstream values cannot inject shell-special
    // characters into the docker-pull command rendered downstream by the React banner
    // (which additionally auto-escapes via JSX text rendering).
    public class GitHubReleasesUpdatePackageProvider : IUpdatePackageProvider
    {
        private const string ReleasesLatestUrl = "https://api.github.com/repos/devbrian/Mangarr/releases/latest";
        private const string ReleasesListUrl = "https://api.github.com/repos/devbrian/Mangarr/releases";

        // Strict version-token guard for tag_name. Accepts optional leading `v` plus
        // up-to-4 dotted numeric components. Anything else is rejected as malformed
        // (T-29-02-01 mitigation).
        private static readonly System.Text.RegularExpressions.Regex TagVersionRegex =
            new System.Text.RegularExpressions.Regex(@"^v?(\d+\.\d+\.\d+(?:\.\d+)?)$",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public GitHubReleasesUpdatePackageProvider(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public UpdatePackage GetLatestUpdate(string branch, Version currentVersion)
        {
            var release = FetchLatestRelease();
            if (release == null)
            {
                return null;
            }

            if (release.Prerelease)
            {
                // /releases/latest excludes prereleases by API contract; defensive guard
                // for the list-endpoint variant.
                return null;
            }

            var latestVersion = ParseTagToSemver(release.TagName);
            if (latestVersion == null)
            {
                _logger.Warn("GitHub Releases broker: could not parse tag_name '{0}' as a valid version; skipping", release.TagName);
                return null;
            }

            var currentSemver = ToSemver(currentVersion);
            if (latestVersion <= currentSemver)
            {
                // Already on (or ahead of) the latest release line — no banner.
                return null;
            }

            return MapToUpdatePackage(release, latestVersion, branch);
        }

        public List<UpdatePackage> GetRecentUpdates(string branch, Version currentVersion, Version previousVersion = null)
        {
            // v1.2 minimum: surface the single latest release. Sonarr's RecentUpdateProvider
            // consumer (frontend Updates page) renders whatever the broker returns —
            // returning the latest entry is enough to satisfy the "Update Available"
            // banner branch in Updates.tsx. Multi-release history is a v1.3+ enhancement
            // if user feedback surfaces it.
            var latest = GetLatestUpdate(branch, currentVersion);
            return latest != null
                ? new List<UpdatePackage> { latest }
                : new List<UpdatePackage>();
        }

        private GitHubReleaseResponse FetchLatestRelease()
        {
            try
            {
                var request = new HttpRequestBuilder(ReleasesLatestUrl).Build();

                // GitHub API requires a User-Agent header for all requests (anonymous use
                // returns 403 without one). Use the canonical Mangarr UA shape mirrored
                // from MangaDexImportListProxy.ApplySharedHeaders.
                request.Headers["User-Agent"] = $"Mangarr/{BuildInfo.Version.ToString(2)}";
                request.Headers["Accept"] = "application/vnd.github+json";

                var response = _httpClient.Get(request);
                if (response == null || string.IsNullOrWhiteSpace(response.Content))
                {
                    _logger.Warn("GitHub Releases broker: empty response from {0}", ReleasesLatestUrl);
                    return null;
                }

                return JsonConvert.DeserializeObject<GitHubReleaseResponse>(response.Content);
            }
            catch (HttpException ex)
            {
                _logger.Warn(
                    "GitHub Releases broker: HTTP {0} fetching {1} — {2}",
                    (int?)ex.Response?.StatusCode ?? 0,
                    ReleasesLatestUrl,
                    ex.Message);
                return null;
            }
            catch (JsonException ex)
            {
                _logger.Warn(
                    "GitHub Releases broker: malformed JSON response from {0} — {1}",
                    ReleasesLatestUrl,
                    ex.Message);
                return null;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "GitHub Releases broker: unexpected error fetching {0}", ReleasesLatestUrl);
                return null;
            }
        }

        private UpdatePackage MapToUpdatePackage(GitHubReleaseResponse release, Version latestVersion, string branch)
        {
            // Pick the matching-runtime asset. Mangarr release tarballs follow the
            // Mangarr.<branch>.<version>.<runtime>.tar.gz naming from
            // .github/actions/package/package.sh — fall back to the first .tar.gz
            // asset (or any asset) if no exact runtime match is found. The banner
            // doesn't itself trigger a download (D-04 banner-only); the asset
            // metadata is informational.
            var preferredAsset = release.Assets?.FirstOrDefault(a =>
                !string.IsNullOrEmpty(a?.Name) &&
                a.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase));

            var anyAsset = preferredAsset ?? release.Assets?.FirstOrDefault();

            return new UpdatePackage
            {
                Version = latestVersion,
                ReleaseDate = release.PublishedAt,
                FileName = anyAsset?.Name,
                Url = anyAsset?.BrowserDownloadUrl ?? release.HtmlUrl,
                HtmlUrl = release.HtmlUrl,
                Branch = branch,
                Hash = null,
                Changes = null
            };
        }

        private static Version ParseTagToSemver(string tagName)
        {
            if (string.IsNullOrWhiteSpace(tagName))
            {
                return null;
            }

            var match = TagVersionRegex.Match(tagName.Trim());
            if (!match.Success)
            {
                return null;
            }

            // Strip optional leading `v` and trailing 4th component to 3-part semver per D-03.
            var versionToken = match.Groups[1].Value;
            if (!Version.TryParse(versionToken, out var parsed))
            {
                return null;
            }

            return ToSemver(parsed);
        }

        private static Version ToSemver(Version v)
        {
            // 4-part Version → 3-part semver per D-03. System.Version normalizes
            // missing components to -1 — clamp Build to 0 for 2-part inputs.
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }

        // Mirrors the GitHub Releases API "release" object shape. Only the fields the
        // broker consumes are modeled — extra fields on the wire are ignored by
        // Newtonsoft.Json default behavior.
        private class GitHubReleaseResponse
        {
            [JsonProperty("tag_name")]
            public string TagName { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("html_url")]
            public string HtmlUrl { get; set; }

            [JsonProperty("published_at")]
            public DateTime PublishedAt { get; set; }

            [JsonProperty("prerelease")]
            public bool Prerelease { get; set; }

            [JsonProperty("assets")]
            public List<GitHubReleaseAsset> Assets { get; set; }
        }

        private class GitHubReleaseAsset
        {
            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("browser_download_url")]
            public string BrowserDownloadUrl { get; set; }
        }
    }
}
