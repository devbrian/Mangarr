using System;

namespace NzbDrone.Core.Parser.Manga.Model
{
    // Parser output DTO for a manga release-title parse. Field shape locked by
    // 02-CONTEXT.md D-09:
    //   * ChapterNumbers carries the full set for multi-chapter releases (single
    //     releases collapse to [N]; oneshots / unparseable releases return [] with
    //     ChapterType set accordingly per D-09).
    //   * VolumeNumber is display-only — there is NO Volume table (see PROJECT.md
    //     "Volumes / Seasons" Out-of-Scope row).
    //   * TranslatedLanguage is BCP-47 (en/es/ja/...). Indexer-supplied values WIN
    //     over parser-extracted ones per D-10; MangaParsingService.Map consumes
    //     whatever this carries — the precedence is enforced at the indexer-pipeline
    //     boundary in Phase 3, not here.
    //   * ReleaseTitle is preserved verbatim — Phase 5 Custom Format regexes match
    //     against it without parser canonicalization.
    public class ParsedChapterInfo
    {
        public ParsedChapterInfo()
        {
            ChapterNumbers = Array.Empty<decimal>();
            ChapterType = ChapterType.Regular;
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
