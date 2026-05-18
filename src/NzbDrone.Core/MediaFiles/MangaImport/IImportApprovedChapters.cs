using System.Collections.Generic;
using NzbDrone.Core.Download;

namespace NzbDrone.Core.MediaFiles.MangaImport
{
    // Sonarr divergence: NEW manga sibling per Phase 6 PIPELINE-04 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/MediaFiles/EpisodeImport/IImportApprovedEpisodes.cs.
    //
    // Plan 06-08 ProcessMangaCompletedDownloads consumes this; per RESEARCH §Q-3, the
    // service emits ChapterImportedEvent ONLY after both ChapterFile DB commit and
    // filesystem move complete (Pitfall 4 mitigation).
    //
    // Phase 8 cleanup: collapse with IImportApprovedEpisodes when Tv/ deletes.
    public interface IImportApprovedChapters
    {
        // Phase 25 Plan 25-04 Task 7 (D-04 + v2-02) — overwriteExisting batch
        // fallback added as the LAST optional parameter. Per-decision
        // LocalChapter.ExistingFileBehavior wins over this batch-level flag;
        // either input drives the per-row MoveFile overwrite. All existing
        // callers compile unchanged (default = false preserves prior
        // no-destructive-default behavior).
        List<MangaImportResult> Import(
            List<MangaImportDecision> decisions,
            bool newDownload,
            DownloadClientItem downloadClientItem = null,
            bool overwriteExisting = false);
    }
}
