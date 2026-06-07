using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.Parser.Manga
{
    // Single source of truth for manga title canonicalization (02-CONTEXT.md D-05).
    // Used by AddMangaService dedup AND CrossSourceIdResolver (Plan 02-09) — keeping
    // the algorithm in one place prevents drift between the two consumers.
    //
    // Algorithm (locked by D-05 + Claude's Discretion):
    //   1. Drop trailing parenthetical / bracketed alt-title suffix
    //      ("Demon Slayer (Manga)" -> "Demon Slayer")
    //   2. NFKD Unicode normalization (collapses fullwidth to ASCII; eg
    //      "Ｄｅｍｏｎ" -> "Demon")
    //   3. Strip combining diacritic marks ("Café" -> "Cafe")
    //   4. Lowercase + strip non-letter/non-digit/non-whitespace characters
    //      (punctuation removed with NO replacement char, so "My/Hero!Academia?"
    //      collapses to "myheroacademia" without inserting spaces mid-word)
    //   5. Collapse whitespace runs
    //
    // CJK ideographs (鬼滅の刃) flow through unchanged: they are categorized as
    // letters by char.IsLetter, and ToLowerInvariant is a no-op for them.
    //
    // SEARCH-QUERY VARIANT (NormalizeForSearch): identical pipeline EXCEPT step 4
    // replaces stripped punctuation with a SPACE instead of removing it with no
    // replacement char. Use this — NOT Normalize — when building an outbound query
    // for an EXTERNAL tokenizing full-text engine (MangaDex/AniList/MAL). Those
    // engines indexed their titles with their own tokenizer, so the comparison-
    // canonicalization "merge adjacent words" rule that is harmless for SYMMETRIC
    // in-DB comparison silently breaks the query against an ASYMMETRIC remote index
    // (debug session chick-class-hunter-search-miss, 2026-06-07): "Chick-Class
    // Hunter" must reach MangaDex as the tokens chick/class/hunter, not as the
    // single non-existent token "chickclass".
    public static class MangaTitleNormalizer
    {
        // Drops trailing parenthetical or bracketed alt-title suffix only — NOT
        // mid-string parens (so `Re:Zero (Light Novel) — Ch.1` would keep the
        // mid-string paren, but the parser strips brackets via its own pipeline).
        private static readonly Regex AltSuffixRegex =
            new(@"\s*[\(\[][^\)\]]*[\)\]]\s*$", RegexOptions.Compiled);

        /// <summary>
        /// Canonical comparison form — the single source of truth for SYMMETRIC
        /// in-DB title matching (AddManga dedup, CrossSourceIdResolver fuzzy match,
        /// FindByTitle / FindByAlternativeTitle). Strips punctuation with NO
        /// replacement char so "My/Hero!Academia?" → "myheroacademia". Do NOT use
        /// this to build a query for an external search engine — see
        /// <see cref="NormalizeForSearch"/>.
        /// </summary>
        public static string Normalize(string title)
            => NormalizeInternal(title, replacePunctuationWithSpace: false);

        /// <summary>
        /// Search-query form for EXTERNAL tokenizing full-text engines
        /// (MangaDex/AniList/MAL). Identical to <see cref="Normalize"/> EXCEPT
        /// stripped punctuation becomes a SPACE so intra-word punctuation does NOT
        /// merge adjacent words into one token: "Chick-Class Hunter" →
        /// "chick class hunter" (matches MangaDex's chick/class/hunter token
        /// index) rather than "chickclass hunter" (matches nothing). Fixes debug
        /// session chick-class-hunter-search-miss (2026-06-07).
        /// </summary>
        public static string NormalizeForSearch(string title)
            => NormalizeInternal(title, replacePunctuationWithSpace: true);

        private static string NormalizeInternal(string title, bool replacePunctuationWithSpace)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return string.Empty;
            }

            // 1. Drop trailing alt-title parenthetical / bracketed suffix.
            title = AltSuffixRegex.Replace(title, string.Empty);

            // 2. NFKD: fullwidth -> ASCII, ligatures decomposed, diacritics split off
            //    to combining marks we strip in step 3.
            var nfkd = title.Normalize(NormalizationForm.FormKD);

            // 3. Strip combining diacritic marks while preserving the base letter.
            var sb = new StringBuilder(nfkd.Length);
            foreach (var c in nfkd)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                }
            }

            // 4. Lowercase + strip punctuation. For the comparison form (Normalize)
            //    punctuation is dropped with NO replacement char so mid-word
            //    punctuation collapses without inserting spaces. For the search form
            //    (NormalizeForSearch) it is replaced with a space so word boundaries
            //    survive for the remote tokenizer.
            var lowered = sb.ToString().ToLowerInvariant();
            var stripped = new StringBuilder(lowered.Length);
            foreach (var c in lowered)
            {
                if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
                {
                    stripped.Append(c);
                }
                else if (replacePunctuationWithSpace)
                {
                    stripped.Append(' ');
                }
            }

            // 5. Collapse whitespace runs into single spaces, trim ends.
            var parts = stripped.ToString().Split(
                new[] { ' ', '\t', '\r', '\n' },
                System.StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts);
        }
    }
}
