namespace NzbDrone.Core.ImportLists
{
    // Sonarr divergence: Phase 26 Plan 26-04 (Pitfall 6) — Plex/Trakt/Simkl values
    // dropped (Sonarr-only; no manga peers). Phase 27 adds MangaDex/AniList/MyAnimeList
    // values when each provider lands. Reference shape preserved at
    // .planning/reference/sonarr-vertical-slices/import-lists/ImportListType.cs.
    //
    // Phase 27 Plan 27-02: MangaDex member added (follows-list provider). Plans 27-03/04
    // will add AniList + MyAnimeList; the values are reserved here pre-emptively so the
    // per-plan diffs stay small and merge-conflict-free during parallel wave 2 execution.
    public enum ImportListType
    {
        Program,
        Other,
        Advanced,
        MangaDex,
        AniList,
        MyAnimeList
    }
}
