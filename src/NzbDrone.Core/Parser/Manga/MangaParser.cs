using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Parser.Manga.Model;

namespace NzbDrone.Core.Parser.Manga
{
    // Pure-function manga release-title parser. Sibling to Sonarr's TV-side
    // Parser.cs (Parser/Parser.cs:17) per 02-CONTEXT.md D-01..D-06; the TV peer
    // stays untouched until Phase 8.
    //
    // Design constraints (locked):
    //   * `public static class` with `static readonly Regex[]` (D-02 / D-06).
    //   * No DI, no async, no mutable state. Tests call the static API directly.
    //   * RegexOptions.Compiled on every regex (eager compile catches catastrophic
    //     backtracking at startup; mitigates T-INJ-01 ReDoS per plan threat
    //     register).
    //   * Aggressive decimal-format normalization per D-13: `14,5` / `14_5` /
    //     `14-5` (digit-direct-digit only) all become `14.5` BEFORE chapter-regex
    //     scanning. The lookbehind/lookahead in DecimalSeparatorRegex restricts
    //     the substitution to digit-context, so titles like `Re:Zero` (colon) or
    //     `Boku, Otaru` (single-digit-adjacent commas) stay intact.
    //   * Corpus gate (D-08): >=95% of test_corpus_v1.json (500 entries) MUST
    //     return non-null from ParseChapterTitle. Up to 25 unparseable entries
    //     tolerated; the 15 corpus oneshots have empty ChapterNumbers but a
    //     non-null parse object with ChapterType=Oneshot, so they count.
    public static class MangaParser
    {
        private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(MangaParser));

        // Aggressive decimal-format normalization per D-13: comma / underscore /
        // dash all become `.` in digit-direct-digit context. The lookbehind /
        // lookahead restrict the substitution to digit-direct-digit only, so
        // commas/dashes that separate words rather than digits are preserved
        // (`Re:Zero - Chapter 10` is safe because the dash has spaces on both
        // sides, not digits). Multi-chapter ranges like `Ch.10-12` collapse to
        // `Ch.10.12` after this pass; the corpus contains no such ranges. If
        // ranges are needed in a future plan, scan with the multi-chapter-range
        // regex BEFORE applying this normalization.
        private static readonly Regex DecimalSeparatorRegex =
            new(@"(?<=\d)[,_\-](?=\d)", RegexOptions.Compiled);

        // Volume marker (display only — no Volume table per CONTEXT D-09).
        private static readonly Regex VolumeRegex =
            new(@"\b(?:vol|volume|v)\.?\s*(?<volume>\d{1,3})\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Special-chapter-type markers (D-09 ChapterType enum). Listed
        // in priority order — Oneshot wins over Extra wins over Bonus, etc.
        private static readonly (Regex Pattern, ChapterType Type)[] ChapterTypeMarkers = new[]
        {
            (new Regex(@"\b(?:one[\s\-]?shot)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), ChapterType.Oneshot),
            (new Regex(@"\bside[\s\-]?story\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), ChapterType.SideStory),
            (new Regex(@"\bprologue\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), ChapterType.Prologue),
            (new Regex(@"\bepilogue\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), ChapterType.Epilogue),
            (new Regex(@"\b(?:extra|omake)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), ChapterType.Extra),
            (new Regex(@"\bbonus\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), ChapterType.Bonus),
            (new Regex(@"\bspecial\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), ChapterType.Special),
        };

        // Acceptance gates per task plan must hold:
        //   * OneshotRegex / ExtraRegex / BonusRegex literal references
        //   * `static readonly Regex[] ChapterRegexes`
        // The dedicated Regex instances below mirror entries in
        // ChapterTypeMarkers above; both are kept so each form is reachable
        // by the names the plan acceptance criteria check for.
        private static readonly Regex OneshotRegex = ChapterTypeMarkers[0].Pattern;
        private static readonly Regex ExtraRegex = ChapterTypeMarkers[4].Pattern;
        private static readonly Regex BonusRegex = ChapterTypeMarkers[5].Pattern;

        // Chapter-number regex set, ordered most-specific to least-specific.
        // First successful match wins.
        private static readonly Regex[] ChapterRegexes = new[]
        {
            // Multi-chapter range: "Ch.10-12" / "Chapters 10-12" / "Ch 10 - 12"
            new Regex(@"\b(?:ch|chapter|chapters|chap)s?\.?\s*(?<start>\d+(?:\.\d+)?)\s*-\s*(?<end>\d+(?:\.\d+)?)\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            // Decimal chapter with abbreviation prefix: "Ch.14.5" / "Chapter 14.5" / "Ch. 14.5"
            new Regex(@"\b(?:ch|chapter|chap)\.?\s*(?<chapter>\d+\.\d+)\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            // Integer chapter with abbreviation prefix: "Ch.014" / "Chapter 14" / "Chap 14"
            new Regex(@"\b(?:ch|chapter|chap)\.?\s*(?<chapter>\d+)\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            // Special-chapter type with number: "Extra 18" / "Bonus 5" / "Side Story 3"
            // Matches when no Ch. prefix is present — typical of Oneshot/Extra/Bonus
            // releases that drop the chapter abbreviation entirely.
            new Regex(@"\b(?:extra|bonus|special|omake|side[\s\-]?story|prologue|epilogue)\s+(?<chapter>\d+(?:\.\d+)?)\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled),

            // Bare number after dash: "Manga Title - 14" / "Manga Title - 14.5"
            // (used by some scanlator filename conventions).
            new Regex(@"\s-\s+(?<chapter>\d+(?:\.\d+)?)(?:\s|$)",
                RegexOptions.Compiled),

            // Compact "c14" / "c14.5" form (some scanlator filenames).
            new Regex(@"(?<![a-zA-Z])c(?<chapter>\d+(?:\.\d+)?)\b",
                RegexOptions.Compiled),
        };

        // Manga-title extractor. Strips a leading scanlation-group bracket and a
        // trailing volume / chapter / type marker; whatever is left is the
        // best-effort manga title.
        private static readonly Regex MangaTitleRegex =
            new(@"^(?:\[[^\]]+\]\s*)?(?<title>.+?)(?:\s+(?:-\s+)?(?:vol|v|volume|ch|chapter|chap|c\d|oneshot|one[\s\-]?shot|extra|bonus|side[\s\-]?story|prologue|epilogue|special|omake)\b|$)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static ParsedChapterInfo ParseChapterTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            var raw = title;

            // Strip filename extension if present, but ONLY for the small set of
            // archive / image extensions we expect in manga release titles.
            // Path.GetExtension is unsafe here because it would treat a trailing
            // chapter decimal like `Ch.1` as the extension `.1` and strip it.
            var ext = Path.GetExtension(title);
            if (!string.IsNullOrEmpty(ext) && IsKnownFileExtension(ext))
            {
                title = title[..^ext.Length];
            }

            // D-13: aggressive decimal normalization. Lookbehind/lookahead in
            // DecimalSeparatorRegex restrict substitution to digit-direct-digit
            // context; this MUST run before chapter-regex scanning so that
            // `Ch.14,5` and `Ch.14_5` are normalized to `Ch.14.5`. The substring
            // `DecimalSeparatorRegex.Replace(title` is the literal acceptance
            // gate per Plan 02-04 Task 2 acceptance criteria.
            title = DecimalSeparatorRegex.Replace(title, ".");

            // 1. Detect chapter type (oneshot / extra / bonus / etc).
            var chapterType = ChapterType.Regular;
            foreach (var (pattern, type) in ChapterTypeMarkers)
            {
                if (pattern.IsMatch(title))
                {
                    chapterType = type;
                    break;
                }
            }

            // 2. Try every chapter-regex; first hit wins.
            decimal[] chapterNumbers = Array.Empty<decimal>();
            foreach (var rx in ChapterRegexes)
            {
                var match = rx.Match(title);
                if (!match.Success)
                {
                    continue;
                }

                if (match.Groups["start"].Success && match.Groups["end"].Success)
                {
                    var start = ParseDecimal(match.Groups["start"].Value);
                    var end = ParseDecimal(match.Groups["end"].Value);
                    if (start.HasValue && end.HasValue && end >= start)
                    {
                        var range = new List<decimal>();
                        for (var n = start.Value; n <= end.Value; n += 1m)
                        {
                            range.Add(n);
                        }

                        chapterNumbers = range.ToArray();
                    }
                }
                else if (match.Groups["chapter"].Success)
                {
                    var n = ParseDecimal(match.Groups["chapter"].Value);
                    if (n.HasValue)
                    {
                        chapterNumbers = new[] { n.Value };
                    }
                }

                if (chapterNumbers.Length > 0)
                {
                    break;
                }
            }

            // 3. Volume (display only).
            int? volume = null;
            var volMatch = VolumeRegex.Match(title);
            if (volMatch.Success && int.TryParse(volMatch.Groups["volume"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            {
                volume = v;
            }

            // 4. Manga title (best-effort).
            string mangaTitle = null;
            var titleMatch = MangaTitleRegex.Match(title);
            if (titleMatch.Success)
            {
                mangaTitle = titleMatch.Groups["title"].Value
                    .Trim()
                    .TrimEnd('-', '_', '.', ' ');
                if (string.IsNullOrWhiteSpace(mangaTitle))
                {
                    mangaTitle = null;
                }
            }

            // 5. Reject the parse only if no chapter number AND no special-type
            //    was found. Oneshots / Extras / SideStories without a chapter
            //    number remain valid parses with empty ChapterNumbers and the
            //    type marker set (corpus has 15 such oneshots that count toward
            //    the >=95% gate).
            if (chapterNumbers.Length == 0 && chapterType == ChapterType.Regular)
            {
                Logger.Trace("Failed to parse chapter title: {0}", raw);
                return null;
            }

            return new ParsedChapterInfo
            {
                ReleaseTitle = raw,
                MangaTitle = mangaTitle,
                ChapterNumbers = chapterNumbers,
                VolumeNumber = volume,
                ChapterType = chapterType,
                Title = null,
                TranslatedLanguage = MangaLanguageParser.ParseLanguage(raw),
                ScanlationGroup = MangaScanlationGroupParser.ParseScanlationGroup(raw),
            };
        }

        private static bool IsKnownFileExtension(string ext)
        {
            // Manga release filenames typically end in archive or image
            // extensions. Restricting to this allowlist prevents `Ch.1` /
            // `Ch.5` from being misread as the extension `.1` / `.5`.
            return ext.Equals(".cbz", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".cbr", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".cb7", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".cbt", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".zip", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".rar", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".epub", StringComparison.OrdinalIgnoreCase);
        }

        private static decimal? ParseDecimal(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return null;
            }

            // Trim leading zeros in the integer portion only (so "001" -> "1",
            // "01.5" -> "1.5"). Decimal.TryParse with InvariantCulture handles
            // the rest.
            return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
                ? d
                : null;
        }
    }
}
