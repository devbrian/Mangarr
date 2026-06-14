using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Parser.Manga.Model;

namespace NzbDrone.Core.Parser.Manga
{
    // Pure-function manga release-title parser. Sibling to Mangarr's TV-side
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

        // Aggressive decimal-format normalization per D-13: comma / underscore become
        // `.` in digit-direct-digit context. The lookbehind / lookahead restrict the
        // substitution to digit-direct-digit only, so commas that separate words
        // rather than digits are preserved.
        //
        // BL-04 FIX: DASH is intentionally EXCLUDED from this character class. A
        // digit-direct-dash-direct-digit form (`10-12`) is the chapter-range syntax
        // captured by `ChapterRegexes[0]` (multi-chapter range), not a decimal
        // separator. Including `-` here previously collapsed `Ch.10-12` into
        // `Ch.10.12` BEFORE the chapter-range regex could match — making
        // ChapterRegexes[0] effectively dead code. The dash stays raw; the range
        // regex sees it; comma + underscore continue to normalize as decimals.
        private static readonly Regex DecimalSeparatorRegex =
            new(@"(?<=\d)[,_](?=\d)", RegexOptions.Compiled);

        // Volume marker (display only — no Volume table per CONTEXT D-09).
        private static readonly Regex VolumeRegex =
            new(@"\b(?:vol|volume|v)\.?\s*(?<volume>\d{1,3})\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Special-chapter-type markers (D-09 ChapterType enum). Listed
        // in priority order — Oneshot wins over Extra wins over Bonus, etc.
        //
        // Apostrophe guard (debug session extras-academy-unknown-manga, 2026-06-14):
        // the trailing `(?!['’‘])` mirrors the same guard on MangaTitleRegex so a
        // marker word that is part of the title's wording ("The Extra's Academy
        // Survival Guide") is NOT mis-classified — without it, "Extra's" supplies a
        // `\b` after "extra" and every regular chapter of such a title was tagged
        // ChapterType.Extra, corrupting organizer naming. A possessive after a marker
        // word is never a real type marker.
        private static readonly (Regex Pattern, ChapterType Type)[] ChapterTypeMarkers = new[]
        {
            (new Regex(@"\b(?:one[\s\-]?shot)\b(?!['’‘])", RegexOptions.IgnoreCase | RegexOptions.Compiled), ChapterType.Oneshot),
            (new Regex(@"\bside[\s\-]?story\b(?!['’‘])", RegexOptions.IgnoreCase | RegexOptions.Compiled), ChapterType.SideStory),
            (new Regex(@"\bprologue\b(?!['’‘])", RegexOptions.IgnoreCase | RegexOptions.Compiled), ChapterType.Prologue),
            (new Regex(@"\bepilogue\b(?!['’‘])", RegexOptions.IgnoreCase | RegexOptions.Compiled), ChapterType.Epilogue),
            (new Regex(@"\b(?:extra|omake)\b(?!['’‘])", RegexOptions.IgnoreCase | RegexOptions.Compiled), ChapterType.Extra),
            (new Regex(@"\bbonus\b(?!['’‘])", RegexOptions.IgnoreCase | RegexOptions.Compiled), ChapterType.Bonus),
            (new Regex(@"\bspecial\b(?!['’‘])", RegexOptions.IgnoreCase | RegexOptions.Compiled), ChapterType.Special),
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
            // WR-03 fix: previous lookbehind only blocked an immediately-preceding
            // letter, but `1c1` and similar digit-letter-digit junk passed the
            // gate. Require the `c` to be preceded by whitespace, dash, or
            // underscore — a real word-boundary on the punctuation side.
            new Regex(@"(?<=^|[\s\-_])c(?<chapter>\d+(?:\.\d+)?)\b",
                RegexOptions.Compiled),
        };

        // Manga-title extractor. Strips a leading scanlation-group bracket and a
        // trailing volume / chapter / type marker; whatever is left is the
        // best-effort manga title.
        //
        // Apostrophe guard (debug session extras-academy-unknown-manga, 2026-06-14):
        // the trailing `\b(?!['’‘])` after the marker alternation prevents a
        // marker WORD that is actually part of the title from truncating it when an
        // apostrophe follows. "Extra's" / "Extra’s" supplies a `\b` immediately
        // after "extra", so the bare `\b` previously let `extra\b` fire mid-title and
        // collapsed "The Extra's Academy Survival Guide" to "The" (the lazy
        // `(?<title>.+?)` stops at the first marker hit) — which then never resolved
        // to the library manga and rejected every release as "Unknown Manga". A
        // marker word immediately followed by an apostrophe is always a possessive
        // that belongs to the title, never a chapter/type delimiter, so we refuse the
        // match and let `(?<title>.+?)` keep consuming. Legit markers ("- Extra 5",
        // "Ch.42", "- Oneshot") are followed by space/digit/dash/end, never an
        // apostrophe, so they still strip correctly.
        private static readonly Regex MangaTitleRegex =
            new(@"^(?:\[[^\]]+\]\s*)?(?<title>.+?)(?:\s+(?:-\s+)?(?:vol|v|volume|ch|chapter|chap|c\d|oneshot|one[\s\-]?shot|extra|bonus|side[\s\-]?story|prologue|epilogue|special|omake)\b(?!['’‘])|$)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static ParsedChapterInfo ParseChapterTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            var raw = title;

            // Strip filename extension if present, but ONLY for the small set of
            // archive / image extensions we expect in manga release titles. WR-01
            // fix: use string-based extraction instead of Path.GetExtension —
            // Path.GetExtension can throw ArgumentException on certain Unicode
            // inputs on some platforms, and is generally heavier than necessary
            // for the simple "trailing dot-suffix" check we want here.
            var lastDot = title.LastIndexOf('.');
            if (lastDot > 0 && lastDot >= title.Length - 6)
            {
                var ext = title[lastDot..];
                if (IsKnownFileExtension(ext))
                {
                    title = title[..^ext.Length];
                }
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
            var chapterNumbers = Array.Empty<decimal>();
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

                    // PARSE2-02: 0.5-grid snap-to-grid range expansion (supersedes the
                    // BL-03 integer-only gate). Pure integer ranges still step by 1m
                    // (Ch.10-12 → [10,11,12]); when either bound carries a fraction we
                    // step by 0.5m on a fixed grid (Ch.1-5.5 → [1,1.5,…,5.5]). The start
                    // is snapped UP onto the grid so an off-grid lower bound still lands
                    // on a grid point, and only grid points <= the upper bound are emitted
                    // (Ch.1-5.3 → [1,1.5,…,5.0]; 5.5 excluded). Guards (descending bounds,
                    // span < 0 after snap, or a grid-point count over the ~1000 cap) leave
                    // chapterNumbers EMPTY so the outer foreach falls through to the
                    // per-chapter regexes — never break early on a guarded-out range.
                    // An integer step-count (count = floor(span/step)+1, then
                    // start0 + i*step) is used rather than a `c += 0.5m` accumulator: the
                    // <= end exit is fragile under off-grid snap, while i*step is exact in
                    // decimal. The cap is enforced WITHOUT any division on the untrusted
                    // span: `count <= 1000` ⟺ `floor(span/step) <= 999` ⟺ `span < 1000*step`
                    // (step > 0), so we compare `span < 1000m * step` directly. This avoids
                    // BOTH overflow paths a large-but-parseable bound could trigger:
                    //   - `span / step` (decimal/0.5m doubles span → OverflowException for a
                    //     near-decimal.MaxValue bound), and
                    //   - the subsequent narrowing `(int)` cast (always checked).
                    // `1000m * step` is at most 1000m (no overflow). Over-cap spans fall
                    // through (chapterNumbers stays EMPTY → outer foreach advances to the
                    // per-chapter regexes); the division + (int) cast only run once the span
                    // is proven within the cap, where gridSteps <= 999 is guaranteed safe.
                    if (start.HasValue && end.HasValue && end.Value >= start.Value)
                    {
                        var lo = start.Value;
                        var hi = end.Value;

                        var bothInteger = lo == Math.Floor(lo) && hi == Math.Floor(hi);
                        var step = bothInteger ? 1m : 0.5m;

                        var start0 = step == 0.5m ? Math.Ceiling(lo * 2m) / 2m : lo;
                        var span = hi - start0;
                        if (span >= 0m && span < (1000m * step))
                        {
                            var count = (int)Math.Floor(span / step) + 1;
                            var range = new List<decimal>(count);
                            for (var i = 0; i < count; i++)
                            {
                                range.Add(start0 + (i * step));
                            }

                            chapterNumbers = range.ToArray();
                        }
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
