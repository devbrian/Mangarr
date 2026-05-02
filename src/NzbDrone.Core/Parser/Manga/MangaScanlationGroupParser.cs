using System.Text.RegularExpressions;

namespace NzbDrone.Core.Parser.Manga
{
    // Scanlation-group extractor. Mirrors Sonarr's anime-style AnimeReleaseGroupRegex
    // (Parser/ReleaseGroupParser.cs:14) per 02-CONTEXT.md D-04 — manga and manhwa
    // releases follow the same `^[<group>] ...` convention as anime, so the regex
    // shape is identical.
    //
    // Matches the FIRST bracketed token at the start of the release title:
    //
    //   "[Mangastream] Naruto - Ch.700"        -> "Mangastream"
    //   "[Multi-word Group] Title - Ch.10"     -> "Multi-word Group"
    //   "Title - Ch.10"                         -> null
    public static class MangaScanlationGroupParser
    {
        // Anchored at start of title; group name must not start or end with
        // whitespace. WR-04 fix: previous pattern used `.+?` between
        // `(?!\s)` and `(?<!\s)`, which silently rejected single-character
        // groups like `[X]` (because `.+?` requires at least one char between
        // the two zero-width anchors, but those anchors themselves consume the
        // FIRST and LAST positions, so a one-char group like `[A]` had no
        // matchable middle). Switching to `[^\]]+?` keeps the no-whitespace
        // anchors (the lookarounds re-test the same character) and now matches
        // single-char group names too.
        private static readonly Regex GroupRegex =
            new(@"^\s*\[(?<subgroup>(?!\s)[^\]]+?(?<!\s))\]",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string ParseScanlationGroup(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            var m = GroupRegex.Match(title);
            return m.Success ? m.Groups["subgroup"].Value : null;
        }
    }
}
