namespace NzbDrone.Core.Manga
{
    // Sonarr divergence: NEW manga sibling per Issue #28 — see DIVERGENCE.md.
    // Role-match analog: NewItemMonitorTypes in Tv/MonitoringOptions.cs (deleted in
    // Phase 15 cutover; canonical reference is Sonarr commit ade40b72b).
    //
    // Two-value enum mirroring Sonarr's `NewItemMonitorTypes { All, None }`. Drives
    // the per-Manga "should new items appearing on subsequent refresh / RSS be
    // auto-monitored?" flag. Persisted as an int column on the Manga table; the
    // frontend single-Manga Edit modal exposes it as a dropdown adjacent to the
    // top-level Monitored checkbox.
    //
    // Manga divergence from TV: TV's enum was added by migration 200 to Series
    // (commit ade40b72b). Manga ships the column at migration 001 baseline per the
    // pre-v1 dev-migration policy (edit 001 in place; see
    // .planning/decisions/dev-migration-policy.md). Default value 0 == All — matches
    // the Sonarr default behaviour where new seasons land monitored.
    public enum MangaMonitorNewItems
    {
        All,
        None
    }
}
