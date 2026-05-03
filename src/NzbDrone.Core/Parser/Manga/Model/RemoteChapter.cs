using System.Collections.Generic;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Manga;

namespace NzbDrone.Core.Parser.Manga.Model
{
    // Resolver output DTO. Mirrors Sonarr's RemoteEpisode shape (Parser/Model/
    // RemoteEpisode.cs) — pairs the parsed release info with the resolved Manga
    // aggregate + the existing Chapter rows that the indexer pipeline will
    // dedup/grab against (Phase 3 hook point per 02-CONTEXT.md D-03).
    //
    // Release carries the source URL once Phase 3 indexers populate it; null in the
    // pure-parse path the unit tests exercise.
    public class RemoteChapter
    {
        public RemoteChapter()
        {
            Chapters = new List<Chapter>();
            CustomFormats = new List<CustomFormat>();
        }

        public string Release { get; set; }
        public ParsedChapterInfo ParsedChapterInfo { get; set; }
        public NzbDrone.Core.Manga.Manga Manga { get; set; }
        public List<Chapter> Chapters { get; set; }

        // Phase 5 — CF augmentation per Phase 5 RESEARCH §"RemoteChapter extensions".
        // Mirrors RemoteEpisode.CustomFormats / .CustomFormatScore (Sonarr precedent).
        // Populated by MangaDownloadDecisionMaker.GetDecisionForReport (Wave 2 plan 05-04).
        public List<CustomFormat> CustomFormats { get; set; }
        public int CustomFormatScore { get; set; }
    }
}
