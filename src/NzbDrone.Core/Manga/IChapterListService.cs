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
        // preserveExistingOnNull (Phase 40 WR-02/IN-02): the DEFAULT (false) keeps the
        // metadata-refresh contract where a null Title/ChapterType is canonical and overwrites
        // verbatim (MangaDexMetadataSource null-EN-title suppression, 2026-05-10). The
        // ChapterSynthesisService caller passes true: a synthesized row carries no authoritative
        // Title/ChapterType (null = "unknown", not "canonically absent"), so on the TOCTOU
        // update branch — a concurrent RefreshMangaCommand inserting the same number with a real
        // title between the synthesis absence-check and this sync — a null synthesized value must
        // NOT clobber the real one.
        void SyncChapters(Manga manga, IEnumerable<Chapter> remoteChapters, bool preserveExistingOnNull = false);
    }
}
