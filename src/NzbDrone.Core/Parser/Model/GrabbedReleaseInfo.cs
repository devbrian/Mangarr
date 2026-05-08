using System.Collections.Generic;

namespace NzbDrone.Core.Parser.Model
{
    // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — EpisodeHistory-based
    // constructor stripped per Plan 15-10 History/EpisodeHistory.cs DELETE. Manga path
    // populates GrabbedReleaseInfo from ReleaseInfo + ChapterIds list directly (Phase 6
    // PIPELINE-04 — see ReleaseInfo.cs comment on the manga sibling field).
    public class GrabbedReleaseInfo
    {
        public string Title { get; set; }
        public string Indexer { get; set; }
        public long Size { get; set; }
        public IndexerFlags IndexerFlags { get; set; }
        public ReleaseType ReleaseType { get; set; }

        public List<int> EpisodeIds { get; set; }

        public GrabbedReleaseInfo()
        {
            EpisodeIds = new List<int>();
        }
    }
}
