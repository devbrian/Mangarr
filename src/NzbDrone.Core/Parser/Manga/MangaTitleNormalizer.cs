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
    public static class MangaTitleNormalizer
    {
        // Drops trailing parenthetical or bracketed alt-title suffix only — NOT
        // mid-string parens (so `Re:Zero (Light Novel) — Ch.1` would keep the
        // mid-string paren, but the parser strips brackets via its own pipeline).
        private static readonly Regex AltSuffixRegex =
            new(@"\s*[\(\[][^\)\]]*[\)\]]\s*$", RegexOptions.Compiled);

        public static string Normalize(string title)
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

            // 4. Lowercase + strip punctuation (NO replacement char) so mid-word
            //    punctuation collapses without inserting spaces.
            var lowered = sb.ToString().ToLowerInvariant();
            var stripped = new StringBuilder(lowered.Length);
            foreach (var c in lowered)
            {
                if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
                {
                    stripped.Append(c);
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
