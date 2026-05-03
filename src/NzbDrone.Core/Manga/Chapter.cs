using System;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Parser.Manga;

namespace NzbDrone.Core.Manga
{
    // Chapter row. Mirrors Sonarr's Tv/Episode.cs shape (precedent: 02-PATTERNS Group 8)
    // adapted for the manga domain per 02-CONTEXT.md D-09:
    //   * ChapterNumber is decimal (DECIMAL(10,3) post-Phase-2 widen — D-12) to support
    //     scanlation-group filename conventions like 1.123.
    //   * VolumeNumber is display-only — no Volumes table exists (PROJECT.md
    //     "Volumes / Seasons" Out-of-Scope row).
    //   * IsSynthetic flags placeholder rows that ChapterListService creates when
    //     MangaDex is not cross-resolved AND the primary returns total-chapter-count
    //     (D-17). Phase 3 indexers fill in fields and flip the flag false on real-feed
    //     match.
    //   * TranslatedLanguage is BCP-47; synthetic rows write "und" sentinel (RESEARCH
    //     §Open Question 3).
    public class Chapter : ModelBase, IComparable
    {
        public int MangaId { get; set; }

        // ChapterNumber — DECIMAL(10,3) per Phase 2 D-12 widen. Index-aware.
        public decimal ChapterNumber { get; set; }
        public decimal? AbsoluteChapterNumber { get; set; }
        public int? VolumeNumber { get; set; }                // display only
        public ChapterType ChapterType { get; set; }

        public string Title { get; set; }
        public string TranslatedLanguage { get; set; }        // BCP-47; "und" for synthetic
        public string ScanlationGroup { get; set; }
        public bool IsSynthetic { get; set; }                 // D-17 marker
        public DateTime? ReleaseDate { get; set; }
        public bool Monitored { get; set; }
        public string ExternalId { get; set; }

        // Phase 6 PIPELINE-04 — FK to ChapterFile row (null = no file imported yet).
        // Sonarr divergence: NEW manga sibling of Episode.EpisodeFileId — see DIVERGENCE.md.
        // Consumed by Plan 06-07 UpgradeSpec + Plan 06-09 Wanted query.
        public int? ChapterFileId { get; set; }

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
