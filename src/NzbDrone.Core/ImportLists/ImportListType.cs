namespace NzbDrone.Core.ImportLists
{
    // Sonarr divergence: Phase 26 Plan 26-04 (Pitfall 6) — Plex/Trakt/Simkl values
    // dropped (Sonarr-only; no manga peers). Phase 27 adds MangaDex/AniList/MyAnimeList
    // values when each provider lands. Reference shape preserved at
    // .planning/reference/sonarr-vertical-slices/import-lists/ImportListType.cs.
    public enum ImportListType
    {
        Program,
        Other,
        Advanced
    }
}
