using System;

namespace NzbDrone.Core.Update
{
    public class UpdatePackage
    {
        public Version Version { get; set; }
        public DateTime ReleaseDate { get; set; }
        public string FileName { get; set; }
        public string Url { get; set; }
        public UpdateChanges Changes { get; set; }
        public string Hash { get; set; }
        public string Branch { get; set; }

        // Phase 29 D-04: GitHub Release page URL (html_url) for the Updates page
        // "View GitHub Release" link. Additive — populated by
        // GitHubReleasesUpdatePackageProvider; left null by other historical paths.
        public string HtmlUrl { get; set; }

        // Phase 29 D-04 (PR #248 review response — Codex P1 #1 + #2): provider-level
        // opt-out for the built-in/script ApplicationUpdate install path. Defaults to
        // true to preserve legacy provider behavior (services.sonarr.tv-style brokers
        // that supply Hash + matching-runtime Url). GitHubReleasesUpdatePackageProvider
        // sets this to false because the broker is banner-only — no SHA256 hash is
        // available without downloading every asset, and the asset chosen at provider
        // time may not match the host's runtime/arch. UpdateController ANDs this flag
        // with the version-newer check before marking the resource installable.
        public bool Installable { get; set; } = true;
    }
}
