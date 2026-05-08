using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Tags
{
    // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption —
    // SeriesIds / ImportListIds / AutoTagIds stripped per Plan 15-03/04/10 deletes.
    // MangaIds added as the manga-shape replacement (referenced by IMangaService.AllForTag,
    // wired in MangaService — Phase 5+).
    public class TagDetails : ModelBase
    {
        public string Label { get; set; }
        public List<int> MangaIds { get; set; }
        public List<int> NotificationIds { get; set; }
        public List<int> RestrictionIds { get; set; }
        public List<int> ExcludedReleaseProfileIds { get; set; }
        public List<int> DelayProfileIds { get; set; }
        public List<int> IndexerIds { get; set; }
        public List<int> DownloadClientIds { get; set; }

        public bool InUse => (MangaIds != null && MangaIds.Any()) ||
                             (NotificationIds != null && NotificationIds.Any()) ||
                             (RestrictionIds != null && RestrictionIds.Any()) ||
                             (ExcludedReleaseProfileIds != null && ExcludedReleaseProfileIds.Any()) ||
                             (DelayProfileIds != null && DelayProfileIds.Any()) ||
                             (IndexerIds != null && IndexerIds.Any()) ||
                             (DownloadClientIds != null && DownloadClientIds.Any());
    }
}
