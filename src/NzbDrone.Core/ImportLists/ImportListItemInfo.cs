using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.ImportLists
{
    // Phase 26 Plan 26-04 — manga-shape POCO authored from scratch per Assumption A1
    // (no preserved file in reference slice). Carries the Mangarr manga-ID triplet
    // (MangaDexId / MalId / AniListId) replacing the Sonarr TVDB/IMDB/TMDB columns.
    //
    // MangaDexId is `string` (not Guid?) to match Migration 003's `.AsString().Nullable()`
    // column shape — Manga.MangaDexId is Guid? in the aggregate POCO but the ImportList
    // entity stores the canonical string serialization so AniList-only / MAL-only imports
    // (no MangaDex match yet) can ride a NULL MangaDexId without round-tripping through
    // Guid.TryParse on every reader.
    //
    // Inherits ModelBase so Dapper can round-trip via the `ImportListItems` table.
    public class ImportListItemInfo : ModelBase
    {
        public string ImplementationName { get; set; }
        public int ImportListId { get; set; }
        public int ServiceProviderId { get; set; }

        // Non-persisted (cleaned per CleanupListItems in ImportListBase): the friendly name
        // of the originating ImportList, populated from Definition.Name at fetch time.
        public string ImportList { get; set; }

        public string Title { get; set; }

        public string MangaDexId { get; set; }
        public int? MalId { get; set; }
        public int? AniListId { get; set; }

        public DateTime ReleaseDate { get; set; }
    }
}
