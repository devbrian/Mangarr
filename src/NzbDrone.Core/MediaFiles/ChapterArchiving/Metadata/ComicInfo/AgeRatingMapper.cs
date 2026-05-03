namespace NzbDrone.Core.MediaFiles.ChapterArchiving.Metadata.ComicInfo
{
    /// <summary>
    /// Phase 4 ARCHIVE-04 — MangaDex <c>contentRating</c> → ComicInfo v2.0
    /// <c>&lt;AgeRating&gt;</c> enum mapping. Source: RESEARCH.md Q-5 (BINDING)
    /// + anansi-project schema v2.0 enum vocabulary.
    /// AniList/MAL adult-flag fallback uses the same mapping (Phase 2 cross-source
    /// resolver normalizes upstream).
    ///
    /// Mapping rationale: <c>safe</c>/<c>suggestive</c> map to ComicInfo's lighter
    /// rating tiers; <c>erotica</c> maps to <c>Adults Only 18+</c> (per Phase 4 plan
    /// frontmatter override on 2026-05-02 — Komga / Kavita lifecycle parity); <c>pornographic</c>
    /// maps to <c>X18+</c> (the most restrictive ComicInfo enum value).
    /// Unknown / null inputs map to <c>Unknown</c> — the canonical sentinel
    /// in ComicInfo v2.0 + v2.1 enums.
    /// </summary>
    public static class AgeRatingMapper
    {
        public static string Map(string mangaDexContentRating)
        {
            return mangaDexContentRating?.ToLowerInvariant() switch
            {
                "safe" => "Everyone",
                "suggestive" => "Teen",
                "erotica" => "Adults Only 18+",
                "pornographic" => "X18+",
                _ => "Unknown",
            };
        }
    }
}
