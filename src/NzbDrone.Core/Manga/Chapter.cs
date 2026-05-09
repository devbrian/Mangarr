using System;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Parser.Manga;

namespace NzbDrone.Core.Manga
{
    // Chapter row. Mirrors Sonarr's Tv/Episode.cs shape (precedent: 02-PATTERNS Group 8)
    // at the (MangaId, ChapterNumber) grain — language-free per Phase 16 STRUCT-01.
    //   * ChapterNumber is decimal (DECIMAL(10,3) per Phase 2 D-12 widen).
    //   * VolumeNumber is display-only — no Volumes table.
    //   * FirstReleaseDate (Phase 16 D-02) is the upstream chapter-publish date —
    //     Sonarr-mirror of Episode.AirDateUtc. Distinct from ChapterRelease.ReleaseDate
    //     (per-translation upload time).
    //   * Per-language data lives on the ChapterRelease sibling (Phase 16 STRUCT-02).
    public class Chapter : ModelBase, IComparable
    {
        public int MangaId { get; set; }

        // ChapterNumber — DECIMAL(10,3) per Phase 2 D-12 widen. Index-aware.
        public decimal ChapterNumber { get; set; }
        public decimal? AbsoluteChapterNumber { get; set; }
        public int? VolumeNumber { get; set; }                // display only
        public ChapterType ChapterType { get; set; }

        public string Title { get; set; }

        // Phase 16 D-02: Sonarr-mirror of Episode.AirDateUtc.
        // Populated by EnsureChapter from the upstream metadata source's chapter-publish date
        // (e.g., MangaDex attrs.PublishAt). NOT derived from ChapterRelease.ReleaseDate
        // (which is per-translation upload time — a distinct concept).
        public DateTime? FirstReleaseDate { get; set; }

        public bool Monitored { get; set; }
        public string ExternalId { get; set; }

        // Phase 6 PIPELINE-04 — FK to ChapterFile row (null = no file imported yet).
        // Sonarr divergence: NEW manga sibling of Episode.EpisodeFileId — see DIVERGENCE.md.
        // Consumed by Plan 06-07 UpgradeSpec + Plan 06-09 Wanted query.
        public int? ChapterFileId { get; set; }

        // Phase 8 audit (EpisodeService-vs-ChapterService.md gap-07) — sibling of TV's
        // `Episode.LastSearchTime`. Recorded by ChapterSearchService / MissingChapterSearchService
        // via `IChapterService.UpdateLastSearchTime` to avoid re-searching every poll cycle.
        // Nullable: null = never searched.
        public DateTime? LastSearchTime { get; set; }

        public int CompareTo(object obj)
        {
            if (obj is not Chapter other)
            {
                return 1;
            }

            return ChapterNumber.CompareTo(other.ChapterNumber);
        }

        public override string ToString()
        {
            return string.Format("[{0}][Manga {1} Ch.{2}]", Id, MangaId, ChapterNumber);
        }
    }
}
