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

        // URL-safe identifier — mirrors Tv/Series.cs:47 TitleSlug. Computed from
        // Title via StringExtensions.ToUrlSlug() in AddMangaService.PrepareForAdd
        // (TV gets it from SkyHook; manga primaries don't expose a slug field, so
        // we derive it locally). Phase 8 audit gap-04 (Series-vs-Manga.md) — frontend
        // /manga/:titleSlug route at MangaDetailsPage.tsx:28-31 requires this end-to-end.
        public string TitleSlug { get; set; }

        // Editorial metadata.
        public string Overview { get; set; }
        public string Status { get; set; }            // ongoing | completed | hiatus | cancelled | deleted
        public string ContentRating { get; set; }
        public List<MediaCover.MediaCover> Images { get; set; }
        public HashSet<int> Tags { get; set; }
        public List<string> Genres { get; set; }

        // Filesystem.
        public string Path { get; set; }
        public string RootFolderPath { get; set; }
        public bool Monitored { get; set; }

        // Issue #28: per-Manga "should new items appearing on subsequent refresh / RSS
        // be auto-monitored?" flag. Mirrors Sonarr's Series.MonitorNewItems
        // (NewItemMonitorTypes enum at Tv/MonitoringOptions.cs in commit ade40b72b).
        // Default 0 == All. Persisted as int column on the Manga table; surfaced on
        // MangaResource and exposed in the single-Manga Edit modal alongside Monitored.
        public MangaMonitorNewItems MonitorNewItems { get; set; }

        // Lifecycle.
        public DateTime Added { get; set; }
        public DateTime? LastInfoSync { get; set; }

        // Cross-source resolver inputs (D-17, D-21 — populated during refresh by the
        // primary metadata source; consumed by CrossSourceIdResolver).
        public int? TotalChapterCount { get; set; }   // D-17 synthesis fallback
        public int? PublicationYear { get; set; }     // D-21 multi-axis confirm
        public string PrimaryAuthor { get; set; }     // D-21 multi-axis confirm

        // Phase 8 audit gap-03 (Series-vs-Manga.md): mirrors Tv/Series.cs:63 AddOptions.
        // Carries the user's post-add monitor + initial-search choices through the
        // AddManga -> MangaScannedHandler -> ChapterMonitoredService chain (sibling
        // wiring lands in cluster 04). Persisted as a JSON column via the
        // IEmbeddedDocument converter auto-registration in TableMapping.RegisterEmbeddedConverter.
        public AddMangaOptions AddOptions { get; set; }

        // Apply user-mutable fields from a refresh / edit. Mirrors AddSeriesService's
        // ApplyChanges pattern (Tv/Series.cs:70-86): canonical IDs (MangaDexId/MalId/
        // AniListId) are immutable post-add and intentionally NOT copied here — manual
        // relink uses the dedicated /api/v5/manga/{id}/links endpoint (Plan 02-09).
        //
        // Phase 8 audit gap-02 (Series-vs-Manga.md): TV's Series.ApplyChanges is
        // dual-purpose — covers BOTH the refresh-merge cycle AND the user-edit cycle
        // (PUT /api/v5/series). Audit chose option (b) — single-method merge — so
        // user-mutable persistence fields (Tags, Monitored, RootFolderPath) are copied
        // here in addition to the metadata fields. Centralizes the contract so the
        // upcoming MangaEditedService bulk-edit fan-out (Phase 8 cluster 04) and the
        // V5 MangaController PUT handler stay in sync as new fields land.
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

            // User-mutable persistence fields — gap-02 backfill.
            Tags = other.Tags;
            Monitored = other.Monitored;
            RootFolderPath = other.RootFolderPath;

            // Issue #28 backfill — three editable fields previously deferred from
            // PR #27's single-Manga Edit modal scope. MonitorNewItems mirrors Sonarr's
            // Series.ApplyChanges line 75 (commit ade40b72b); TranslationProfileId +
            // CustomFormatProfileId mirror Sonarr's Series.QualityProfileId copy at
            // line 76 (one FK was split into two per Phase 5 D-01 + D-07).
            MonitorNewItems = other.MonitorNewItems;
            TranslationProfileId = other.TranslationProfileId;
            CustomFormatProfileId = other.CustomFormatProfileId;

            // Phase 8 audit gap-03 backfill — mirrors Series.ApplyChanges line 85.
            AddOptions = other.AddOptions;
        }

        public override string ToString()
        {
            return string.Format("[{0}][{1}]", Id, Title ?? "(no title)");
        }
    }
}
