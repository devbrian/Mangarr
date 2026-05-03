using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Manga
{
    // Manga aggregate root. Mirrors Sonarr's Tv/Series.cs shape (precedent: 02-PATTERNS
    // Group 8) but diverges on cross-source ID typing per Phase 2 02-CONTEXT.md
    // specifics: Manga has 1:1 canonical MAL/AniList relationships (unlike anime, which
    // uses HashSet<int>). MangaDexId is Guid? because MangaDex IDs are UUIDs (D-09;
    // 02-RESEARCH §Open Question 4).
    //
    // Persistence: shape matches Migration 002 + 001 baseline columns. Dapper handles
    // Guid? <-> string column round-trips through the global GuidConverter that
    // TableMapping registers for Users.Identifier (Sonarr precedent at
    // 001_mangarr_baseline.cs:469-473). HashSet<int> Tags rides the existing
    // EmbeddedDocumentConverter<HashSet<int>> registration.
    public class Manga : ModelBase
    {
        public Manga()
        {
            Images = new List<MediaCover.MediaCover>();
            Genres = new List<string>();
            Tags = new HashSet<int>();
        }

        // External IDs — singular per CONTEXT specifics; manga is 1:1 across sources.
        public Guid? MangaDexId { get; set; }
        public int? MalId { get; set; }
        public int? AniListId { get; set; }

        // Phase 5 — per-Manga profile FK columns.
        // Per Phase 5 D-01 (TranslationProfile) + D-07 (CustomFormatProfile). Both nullable int —
        // fall back to Config.DefaultTranslationProfileId / Config.DefaultCustomFormatProfileId
        // when null. Mirrors Sonarr's Series.QualityProfileId per-Series-FK pattern verbatim.
        // Schema delta in 001_mangarr_baseline.cs.
        public int? TranslationProfileId { get; set; }
        public int? CustomFormatProfileId { get; set; }

        // Sonarr divergence: NEW manga column per Phase 6 D-10 — see DIVERGENCE.md.
        // Three-state semantics: NULL (default) = fall back to per-Profile UpgradeAllowed flag;
        // TRUE = force-allow upgrades on this Manga; FALSE = force-disallow. Mirrors the
        // per-Manga FK + global default pattern Phase 5 D-01 + D-07 established for both
        // profiles. Schema delta in 001_mangarr_baseline.cs.
        public bool? UpgradeAllowedOverride { get; set; }

        // Title fields — mirror Sonarr's Series shape for parser/normalizer reuse.
        public string Title { get; set; }
        public string CleanTitle { get; set; }
        public string SortTitle { get; set; }

        // Editorial metadata.
        public string Overview { get; set; }
        public string Status { get; set; }            // ongoing | completed | hiatus | cancelled
        public string ContentRating { get; set; }
        public List<MediaCover.MediaCover> Images { get; set; }
        public HashSet<int> Tags { get; set; }
        public List<string> Genres { get; set; }

        // Filesystem.
        public string Path { get; set; }
        public string RootFolderPath { get; set; }
        public bool Monitored { get; set; }

        // Lifecycle.
        public DateTime Added { get; set; }
        public DateTime? LastInfoSync { get; set; }

        // Cross-source resolver inputs (D-17, D-21 — populated during refresh by the
        // primary metadata source; consumed by CrossSourceIdResolver).
        public int? TotalChapterCount { get; set; }   // D-17 synthesis fallback
        public int? PublicationYear { get; set; }     // D-21 multi-axis confirm
        public string PrimaryAuthor { get; set; }     // D-21 multi-axis confirm

        // Apply user-mutable fields from a refresh / edit. Mirrors AddSeriesService's
        // ApplyChanges pattern (Tv/Series.cs:70-86): canonical IDs (MangaDexId/MalId/
        // AniListId) are immutable post-add and intentionally NOT copied here — manual
        // relink uses the dedicated /api/v5/manga/{id}/links endpoint (Plan 02-09).
        public void ApplyChanges(Manga other)
        {
            Title = other.Title;
            Overview = other.Overview;
            Status = other.Status;
            ContentRating = other.ContentRating;
            Images = other.Images;
            Genres = other.Genres;
            TotalChapterCount = other.TotalChapterCount;
            PublicationYear = other.PublicationYear;
            PrimaryAuthor = other.PrimaryAuthor;
            LastInfoSync = DateTime.UtcNow;
        }

        public override string ToString()
        {
            return string.Format("[{0}][{1}]", Id, Title ?? "(no title)");
        }
    }
}
