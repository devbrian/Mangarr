using System.Collections.Generic;

namespace NzbDrone.Core.Manga
{
    // Single-pass canonical Chapter upsert — mirrors Sonarr's IRefreshEpisodeService
    // (deleted in commit 7794a184c, but historically:
    //   `void RefreshEpisodeInfo(Series series, IEnumerable<Episode> remoteEpisodes);`).
    // Phase 16's two-method split was collapsed in Phase 16.1 Wave 3 (REVERT-03) per
    // CONTEXT.md D-01 / SPEC.md "revert ChapterListService to single-pass SyncChapters."
    //
    // Stale-handling decision (LOCKED per Phase 16.1 CONTEXT.md additional_context Pitfall #4
    // + PATTERNS.md Pitfall 6): SyncChapters does NOT delete stale chapters. Pre-Phase-16
    // behavior retained; Sonarr's RefreshEpisodeService DOES delete stale, but SPEC scope is
    // "revert," not "Sonarr-canonicalize stale-handling." Defer Sonarr-canonical
    // stale-handling to a future phase if needed.
    public interface IChapterListService
    {
        void SyncChapters(Manga manga, IEnumerable<Chapter> remoteChapters);
    }
}
