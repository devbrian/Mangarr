using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 8 audit (no-sibling/AddSeriesOptions.md) — see DIVERGENCE.md.
    // Mirrors Tv/AddSeriesOptions.cs shape; carries post-add monitor + initial-search choices through
    // AddManga -> MangaScannedHandler -> ChapterMonitoredService chain.
    //
    // Diverges on monitor enum: uses the single canonical 7-value MangaMonitor (per #357 D-2)
    // instead of TV's 13-value MonitorTypes. Search flags are renamed Episode -> Chapter to match
    // manga domain.
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

    // #357 D-2 (supersedes the Phase 6 D-03 5-value lock): the single canonical 7-value monitor
    // enum used across backend, V5 API, and frontend. MonitorTypes / NewItemMonitorTypes (the
    // Sonarr-inherited import-list peers) are DELETED — ImportListDefinition.ShouldMonitor is now
    // typed MangaMonitor too.
    //
    // The EXPLICIT ordinals are LOAD-BEARING: ImportLists.ShouldMonitor is persisted by-ordinal
    // (an AsInt32() column; 001_mangarr_baseline.cs:159) and previously held MonitorTypes ordinals
    // None=0/All=1/Existing=2/Latest=3/First=4. Declaring MangaMonitor with the matching ordinals
    // preserves every previously-persisted int with NO migration (Future/Missing get brand-new
    // ints 5/6). The API wire (JsonStringEnumConverter, Startup.cs) and the Manga.AddOptions
    // embedded JSON both (de)serialize MangaMonitor BY NAME, so the ordinal order is independent
    // of the frontend dropdown display order ('all','future','missing','existing','first','latest',
    // 'none'). The C# declaration order below differs from the CONTEXT.md value list ON PURPOSE to
    // honor the "must not shift persisted/serialized meaning" clause of D-2.
    //
    // Frontend mirror lives at frontend/src/Manga/Manga.ts as the union
    // 'all' | 'future' | 'missing' | 'existing' | 'first' | 'latest' | 'none'.
    public enum MangaMonitor
    {
        None = 0,
        All = 1,
        Existing = 2,
        Latest = 3,
        First = 4,
        Future = 5,
        Missing = 6
    }
}
