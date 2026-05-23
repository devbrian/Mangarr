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
    }
}
