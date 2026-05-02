using System;

namespace NzbDrone.Core.Parser.Manga.Model
{
    // Manga peer of ParsedEpisodeInfo. Field shape per CONTEXT D-09.
    // Wave 0 stub — fields and behavior land in Plan 02-04.
    public class ParsedChapterInfo
    {
        public ParsedChapterInfo()
        {
            ChapterNumbers = Array.Empty<decimal>();
        }

        public string ReleaseTitle { get; set; }
        public string MangaTitle { get; set; }
        public decimal[] ChapterNumbers { get; set; }
        public decimal? AbsoluteChapterNumber { get; set; }
        public int? VolumeNumber { get; set; }
        public ChapterType ChapterType { get; set; }
        public string Title { get; set; }
        public string TranslatedLanguage { get; set; }
        public string ScanlationGroup { get; set; }
    }
}
