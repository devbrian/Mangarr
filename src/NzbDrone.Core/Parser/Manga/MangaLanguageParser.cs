using System.Text.RegularExpressions;

namespace NzbDrone.Core.Parser.Manga
{
    // BCP-47 translated-language extractor for manga release titles. Sibling to
    // Mangarr's TV-side LanguageParser (Parser/LanguageParser.cs) per 02-CONTEXT.md
    // D-04 — the TV peer stays untouched until Phase 8.
    //
    // Algorithm:
    //   1. Spelled-out language names (Spanish / Español / English / etc) win first
    //      because they cannot collide with scanlation-group acronyms.
    //   2. THEN scan all bracket tags `[xx]` / `(xx)` and pick the first one whose
    //      length / shape looks like a BCP-47 code (2 chars, 3 chars, or contains a
    //      hyphen). This handles `[EN]` / `[ja]` / `[en-US]` while skipping the
    //      release-title leading `[Mangastream]` group bracket.
    //
    // Returns a lowercase BCP-47 code or null if no language marker is found.
    // Per requirement LANG-01.
    public static class MangaLanguageParser
    {
        // Bracketed tag of 2-3 alpha chars, optionally region-suffixed (-XX / -XXXX).
        private static readonly Regex BracketedTagRegex =
            new(@"[\[\(](?<tag>[a-zA-Z]{2,3}(?:-[a-zA-Z0-9]{2,4})?)[\]\)]",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Spelled-out language names mapped to BCP-47 codes. Matched as whole words
        // (\b) so `english` matches but `englishify` (hypothetical) would not.
        private static readonly (Regex Pattern, string Bcp47)[] SpelledOut = new[]
        {
            (new Regex(@"\b(english|eng)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "en"),
            (new Regex(@"\b(spanish|espanol|español|castellano)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "es"),
            (new Regex(@"\b(japanese|nihongo|raw)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "ja"),
            (new Regex(@"\b(french|francais|français)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "fr"),
            (new Regex(@"\b(german|deutsch)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "de"),
            (new Regex(@"\b(portuguese|portugues|português)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "pt"),
            (new Regex(@"\b(italian|italiano)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "it"),
            (new Regex(@"\b(korean|hangul)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "ko"),
            (new Regex(@"\b(chinese|simplified|traditional)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), "zh"),
        };

        // Returns BCP-47 code (en / es / ja / ...) or null if no language marker
        // is found. The bracketed-tag scan iterates ALL matches and picks the first
        // shape-matching tag, so the release-title leading `[Mangastream]` group
        // bracket is correctly skipped over in favor of the trailing `[EN]` tag.
        public static string ParseLanguage(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            // WR-02 fix: strip the leading `[Group]` scanlation-group bracket BEFORE
            // running the spelled-out scan. Otherwise `\b(english|eng)\b` would
            // match inside `[English Subbed]` / `[Engineer Translations]` and
            // similar legitimate group names. Same idea for `\braw\b` (real
            // Japanese-raw release marker but a common English word). The bracket
            // pair must be the FIRST non-whitespace token; subsequent brackets
            // (e.g. `[EN]` language tags) are still scanned in step 2 below.
            var scanTitle = StripLeadingGroupBracket(title);

            // 1. Spelled-out language names win.
            foreach (var (pattern, bcp47) in SpelledOut)
            {
                if (pattern.IsMatch(scanTitle))
                {
                    return bcp47;
                }
            }

            // 2. Scan all bracketed tags; pick the first that looks like a real
            //    BCP-47 code rather than a scanlation-group name. We use the
            //    ORIGINAL title here (not scanTitle) so trailing `[EN]` etc. tags
            //    are still picked up; the leading scanlator bracket fails the
            //    LooksLikeLanguageCode shape check naturally.
            foreach (Match m in BracketedTagRegex.Matches(title))
            {
                var tag = m.Groups["tag"].Value.ToLowerInvariant();
                if (!LooksLikeLanguageCode(tag))
                {
                    continue;
                }

                // Map common 3-letter ISO 639-2 forms to their 2-letter ISO 639-1
                // equivalents to keep the output canonical (eng -> en, spa -> es).
                return CanonicalizeIso(tag);
            }

            return null;
        }

        // WR-02 helper: remove the FIRST `[...]` if it appears at the very start of
        // the title (after optional whitespace). This is the scanlation-group bracket
        // and must not be considered when scanning for spelled-out language names.
        private static readonly Regex LeadingGroupBracketRegex =
            new(@"^\s*\[[^\]]*\]\s*", RegexOptions.Compiled);

        private static string StripLeadingGroupBracket(string title)
        {
            return LeadingGroupBracketRegex.Replace(title, string.Empty, 1);
        }

        private static bool LooksLikeLanguageCode(string tag)
        {
            // 2-char alpha code (en / es / ja) — definitely a language code.
            if (tag.Length == 2)
            {
                return true;
            }

            // Region-suffixed (en-US, es-419) — definitely a language code.
            if (tag.Contains('-'))
            {
                return true;
            }

            // 3-char form is ambiguous (could be ISO 639-2 like `eng` or could be
            // a 3-letter scanlation-group acronym). Whitelist common 3-letter ISO
            // 639-2 codes; everything else falls through.
            switch (tag)
            {
                case "eng":
                case "spa":
                case "jpn":
                case "fra":
                case "fre":
                case "ger":
                case "deu":
                case "por":
                case "ita":
                case "kor":
                case "chi":
                case "zho":
                case "rus":
                case "ara":
                case "tha":
                case "vie":
                case "ind":
                    return true;
                default:
                    return false;
            }
        }

        private static string CanonicalizeIso(string tag)
        {
            // Map 3-letter ISO 639-2 forms to 2-letter ISO 639-1 where applicable.
            return tag switch
            {
                "eng" => "en",
                "spa" => "es",
                "jpn" => "ja",
                "fra" or "fre" => "fr",
                "ger" or "deu" => "de",
                "por" => "pt",
                "ita" => "it",
                "kor" => "ko",
                "chi" or "zho" => "zh",
                "rus" => "ru",
                "ara" => "ar",
                "tha" => "th",
                "vie" => "vi",
                "ind" => "id",
                _ => tag,
            };
        }
    }
}
