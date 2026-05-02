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
        // whitespace (mirrors Sonarr's `(?!\s).+?(?<!\s)` precedent).
        private static readonly Regex GroupRegex =
            new(@"^\s*\[(?<subgroup>(?!\s).+?(?<!\s))\]",
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
