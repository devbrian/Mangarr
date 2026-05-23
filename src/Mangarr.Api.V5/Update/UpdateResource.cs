using Mangarr.Http.REST;
using NzbDrone.Core.Update;

namespace Mangarr.Api.V5.Update
{
    public class UpdateResource : RestResource
    {
        public required Version Version { get; set; }

        public required string Branch { get; set; }
        public DateTime ReleaseDate { get; set; }
        public required string FileName { get; set; }
        public required string Url { get; set; }
        public bool Installed { get; set; }
        public DateTime? InstalledOn { get; set; }
        public bool Installable { get; set; }
        public bool Latest { get; set; }
        public required UpdateChanges Changes { get; set; }
        public required string Hash { get; set; }

        // Phase 29 D-04 — GitHub Release page URL for the Updates page "View GitHub
        // Release" link. Nullable on legacy/no-op paths; populated by
        // GitHubReleasesUpdatePackageProvider only.
        public string? HtmlUrl { get; set; }
    }

    public static class UpdateResourceMapper
    {
        public static UpdateResource ToResource(this UpdatePackage model)
        {
            return new UpdateResource
            {
                Version = model.Version,

                Branch = model.Branch,
                ReleaseDate = model.ReleaseDate,
                FileName = model.FileName,
                Url = model.Url,

                // Installed

                // Phase 29 D-04 (PR #248 review): carry model.Installable through so
                // banner-only providers (GitHubReleasesUpdatePackageProvider) can opt
                // out of the built-in install path. UpdateController ANDs this with
                // the version-newer check before the Install Latest button renders.
                Installable = model.Installable,

                // Latest
                Changes = model.Changes,
                Hash = model.Hash,
                HtmlUrl = model.HtmlUrl,
            };
        }

        public static List<UpdateResource> ToResource(this IEnumerable<UpdatePackage> models)
        {
            return models.Select(ToResource).ToList();
        }
    }
}
