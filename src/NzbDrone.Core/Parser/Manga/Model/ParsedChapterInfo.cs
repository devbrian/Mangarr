using System;
using System.Linq;

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

        // Mirror of TV's ParsedEpisodeInfo.ToString (Parser/Model/ParsedEpisodeInfo.cs:117-150).
        // Builds a human-readable label for logs / UI surfaces (queue, activity, status rows).
        // Format: "{MangaTitle} - {chapter-portion} [{TranslatedLanguage}|{ScanlationGroup}]".
        // Optional fields are skipped gracefully so log lines stay legible.
        public override string ToString()
        {
            string chapterPortion;

            switch (ChapterType)
            {
                case ChapterType.Oneshot:
                    chapterPortion = "Oneshot";
                    break;

                default:
                    if (ChapterNumbers != null && ChapterNumbers.Any())
                    {
                        var formatted = ChapterNumbers
                            .Select(c => c.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))
                            .ToArray();

                        chapterPortion = formatted.Length > 1
                            ? string.Format("Ch.{0}-{1}", formatted.First(), formatted.Last())
                            : string.Format("Ch.{0}", formatted[0]);
                    }
                    else if (VolumeNumber.HasValue)
                    {
                        chapterPortion = string.Format("Vol.{0:00}", VolumeNumber.Value);
                    }
                    else
                    {
                        chapterPortion = "[Unknown Chapter]";
                    }

                    break;
            }

            // Tag block: "[lang|group]", with empty optional fields dropped.
            var hasLang = !string.IsNullOrWhiteSpace(TranslatedLanguage);
            var hasGroup = !string.IsNullOrWhiteSpace(ScanlationGroup);

            string tag;
            if (hasLang && hasGroup)
            {
                tag = string.Format(" [{0}|{1}]", TranslatedLanguage, ScanlationGroup);
            }
            else if (hasLang)
            {
                tag = string.Format(" [{0}]", TranslatedLanguage);
            }
            else if (hasGroup)
            {
                tag = string.Format(" [{0}]", ScanlationGroup);
            }
            else
            {
                tag = string.Empty;
            }

            return string.Format("{0} - {1}{2}", MangaTitle, chapterPortion, tag);
        }
    }
}
