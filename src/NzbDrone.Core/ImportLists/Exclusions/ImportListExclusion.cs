using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.ImportLists.Exclusions
{
    // Phase 26 Plan 26-04 — ported from
    // .planning/reference/sonarr-vertical-slices/import-lists/Exclusions/ImportListExclusion.cs
    // with the Sonarr `TvdbId` column replaced by Mangarr's manga-ID triplet
    // (MangaDexId / MalId / AniListId) per Migration 003 (003_v1_1_importlist_substrate
    // _delayprofile_trim.cs:54-57).
    //
    // MangaDexId is `string` (nullable) to match Migration 003's `.AsString().Nullable()`
    // column shape — the persisted canonical form lets AniList-only / MAL-only exclusions
    // (no MangaDex match) ride a NULL MangaDexId per SQLite's UNIQUE-with-NULLs semantics
    // (RESEARCH §Q4). Manga.MangaDexId is `Guid?` in the aggregate POCO; the exclusion
    // service stores `manga.MangaDexId?.ToString()` so the FindByMangaDexId finder can
    // string-compare on the indexed column directly.
    public class ImportListExclusion : ModelBase
    {
        public string MangaDexId { get; set; }
        public int? MalId { get; set; }
        public int? AniListId { get; set; }
        public string Title { get; set; }
    }
}
