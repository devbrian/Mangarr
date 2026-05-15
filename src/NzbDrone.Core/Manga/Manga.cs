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
            AlternativeTitles = new List<string>();
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

        // GH #118 fix — persisted alt-title set, the Mangarr analog of Sonarr's
        // SceneMappingService alias dataset (Sonarr SceneMapping is a community-
        // curated separate table; Mangarr's alt-titles come from the same metadata
        // refresh as everything else on this entity, so a JSON column on Manga is
        // sufficient). Stored pre-normalized via MangaTitleNormalizer.Normalize at
        // write-time so MangaParsingService.GetManga can do an O(1) exact-match
        // lookup against the canonicalized release title (Strategy 2 of the
        // 3-strategy resolution mirroring Sonarr's ParsingService.GetSeries).
        //
        // Populated by MangaDex/AniList/MyAnimeList metadata sources from each
        // provider's alt-title field set (MangaDex attributes.title + altTitles,
        // AniList romaji/english/native + synonyms, MAL alternative_titles).
        // Includes the romanized attributes.title that SelectPreferredTitle
        // currently discards — that romanization is what the indexer feed emits
        // (DEF-19-02-01 root cause), so persisting it here is what restores
        // GetManga's ability to resolve MangaDex search-path releases without
        // the 77a114221 force-assign short-circuit.
        //
        // Serialized via the global StringListConverter<List<string>>
        // registration at TableMapping.cs (same shape as Manga.Genres).
        public List<string> AlternativeTitles { get; set; }

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

            // GH #118 — metadata-sourced alt-title set; refresh-merged when the
            // incoming Manga carries a populated list. ApplyChanges is dual-purpose
            // (refresh-merge from MangaDex/AniList/MAL MapManga AND user-PUT merge
            // from MangaResourceMapper.ToModel) — the metadata-source path supplies
            // a fully-populated list, but the user-PUT path leaves AlternativeTitles
            // at the constructor-default empty list (the API resource intentionally
            // does NOT expose AlternativeTitles since it is parser-internal). Without
            // the IsNullOrEmpty guard, a plain UI Edit would clobber persisted
            // aliases on every PUT and break Strategy-2 resolution in
            // MangaParsingService.GetManga until the next RefreshMangaCommand.
            // Mirrors the Path save/restore defense at MangaController.UpdateManga:212.
            if (other.AlternativeTitles != null && other.AlternativeTitles.Count > 0)
            {
                AlternativeTitles = other.AlternativeTitles;
            }

            LastInfoSync = DateTime.UtcNow;

            // User-mutable persistence fields — gap-02 backfill.
            Tags = other.Tags;
            Monitored = other.Monitored;
            RootFolderPath = other.RootFolderPath;

            // issue #81 bug-fix (2026-05-13): the Path field MUST be copied
            // here so MangaController.UpdateManga's persistence step lands
            // the new on-disk path that MoveMangaCommand just moved files to.
            // Without this copy, the V5 PUT returns Accepted, files actually
            // move on disk, RootFolderPath updates -- but Manga.Path stays at
            // the old (now non-existent) directory, breaking every subsequent
            // ChapterFile import + Rename scan + Refresh. Mirrors upstream
            // Tv/Series.cs:75 Series.ApplyChanges "Path = otherSeries.Path".
            // Surfaced by the issue #81 live on-disk smoke test on 2026-05-13.
            Path = other.Path;

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
