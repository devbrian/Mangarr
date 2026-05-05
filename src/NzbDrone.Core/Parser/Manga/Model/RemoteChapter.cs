using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Parser.Manga.Model
{
    // Resolver output DTO. Mirrors Sonarr's RemoteEpisode shape (Parser/Model/
    // RemoteEpisode.cs) — pairs the parsed release info with the resolved Manga
    // aggregate + the existing Chapter rows that the indexer pipeline will
    // dedup/grab against (Phase 3 hook point per 02-CONTEXT.md D-03).
    //
    // Phase 5 deviation (Rule 2 — Wave 0 substrate gap): Release was previously
    // typed as string. The decision pipeline (specs / comparer / maker) needs the
    // full ReleaseInfo (TranslatedLanguage + IndexerPriority + Size + AgeMinutes
    // + IndexerFlags + Title) so we widened the type to mirror RemoteEpisode.Release
    // (Parser/Model/RemoteEpisode.cs:14). No prior production code reads
    // RemoteChapter.Release as a string (verified across src/ at plan-execute time).
    public class RemoteChapter
    {
        public RemoteChapter()
        {
            Chapters = new List<Chapter>();
            CustomFormats = new List<CustomFormat>();
        }

        public ReleaseInfo Release { get; set; }
        public ParsedChapterInfo ParsedChapterInfo { get; set; }
        public NzbDrone.Core.Manga.Manga Manga { get; set; }
        public List<Chapter> Chapters { get; set; }

        // Phase 5 — CF augmentation per Phase 5 RESEARCH §"RemoteChapter extensions".
        // Mirrors RemoteEpisode.CustomFormats / .CustomFormatScore (Sonarr precedent).
        // Populated by MangaDownloadDecisionMaker.GetDecisionForReport (Wave 2 plan 05-04).
        public List<CustomFormat> CustomFormats { get; set; }
        public int CustomFormatScore { get; set; }

        // WR-08: stamp the resolved CustomFormatProfileId once at the maker (per-Manga FK
        // ?? global default Config.DefaultCustomFormatProfileId) so the downstream
        // CustomFormatMinimumScoreSpecification reads the SAME id the maker used to compute
        // the score. Avoids the consistency hazard where an admin changes the global default
        // between the maker's score-derivation read and the spec's gate read, leaving the
        // score and the gate keyed off different profiles.
        public int? ResolvedCustomFormatProfileId { get; set; }

        // Phase 8 backfill (audit gap-01) — parity with TV RemoteEpisode.IsRecentEpisode
        // (Parser/Model/RemoteEpisode.cs:36-39). Manga uses Chapter.ReleaseDate as the
        // canonical release timestamp (manga analog of Episode.AirDateUtc per CLAUDE.md
        // mapping). Returns true if any chapter was released within the last 14 days.
        public bool IsRecentChapter()
        {
            return Chapters.Any(c => c.ReleaseDate >= DateTime.UtcNow.Date.AddDays(-14));
        }

        public override string ToString()
        {
            return Release == null ? "(no release)" : Release.Title;
        }
    }
}
