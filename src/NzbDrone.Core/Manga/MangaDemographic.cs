namespace NzbDrone.Core.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 24 (v1.1 INSERTED 2026-05-17)
    // D-04 — see DIVERGENCE.md "MangaDemographic enum — MangaDex 4-value source".
    // No Sonarr/Tv peer; manga axis backed by MangaDex publicationDemographic.
    // Stored as INTEGER NULL on Manga table via Migration 002 + Dapper int-enum converter.
    // null = None / not categorized; explicit numeric values start at 1 so accidental
    // int defaults are non-meaningful.
    public enum MangaDemographic
    {
        Shonen = 1,
        Shojo = 2,
        Seinen = 3,
        Josei = 4
    }
}
