using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 8 audit (no-sibling/AddSeriesOptions.md) — see DIVERGENCE.md.
    // Mirrors Tv/AddSeriesOptions.cs shape; carries post-add monitor + initial-search choices through
    // AddManga -> MangaScannedHandler -> ChapterMonitoredService chain.
    //
    // Diverges on monitor enum: uses 5-value MangaMonitor (Phase 6 D-03) instead of TV's 13-value
    // MonitorTypes. Search flags are renamed Episode -> Chapter to match manga domain.
    //
    // Defined as a flat IEmbeddedDocument (not splitting Monitor/Ignore* into a parent
    // MonitoringOptions class as Tv/ does) — manga has no second consumer that would justify the
    // extra type, and the audit gap calls for a single AddMangaOptions surface.
    public class AddMangaOptions : IEmbeddedDocument
    {
        public MangaMonitor Monitor { get; set; }
        public bool SearchForMissingChapters { get; set; }
        public bool SearchForCutoffUnmetChapters { get; set; }
        public bool IgnoreChaptersWithFiles { get; set; }
        public bool IgnoreChaptersWithoutFiles { get; set; }
    }

    // Phase 6 D-03: 5-value enum (vs Sonarr's 13-value MonitorTypes). Frontend mirror lives at
    // frontend/src/Manga/Manga.ts as the union 'all' | 'future' | 'missing' | 'latest' | 'none'.
    public enum MangaMonitor
    {
        All,
        Future,
        Missing,
        Latest,
        None
    }
}
