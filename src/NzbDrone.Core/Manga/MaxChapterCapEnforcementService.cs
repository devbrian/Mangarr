using System;
using System.Linq;
using NLog;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Manga
{
    // Sonarr divergence: NEW-in-Mangarr destructive maintenance handler — no TV analog
    // (TheTVDB owns the canonical episode list; there is no user-set hard ceiling concept).
    // quick-260619-o5q extension (2026-06-19): the user-owned Manga.MaxChapterNumber cap
    // (Migration 016) originally only CLAMPED on-search synthesis (ChapterSynthesisService —
    // it PREVENTED new phantom rows above the ceiling). The user now wants the cap to ALSO
    // CLEAN UP existing chapters above it: a hard ceiling that DELETES Chapter rows AND their
    // downloaded ChapterFiles when the cap is set/changed via the Edit modal.
    //
    // EFFECTIVE DELETION CEILING = max(MaxChapterNumber, TotalChapterCount ?? 0).
    // This MIRRORS the synthesis-clamp contract verbatim: "only the metadata chapter count can
    // raise the effective ceiling above the cap." Flooring at TotalChapterCount means a stale
    // metadata UNDER-count can never trigger deletion of real downloaded extensions below the
    // trusted metadata number — the cap can only ever trim ABOVE the larger of (cap, metadata).
    // Example confirmed with the user: cap=34, metadata=38 → delete all chapters numbered > 38.
    //
    // TRIGGER DISCIPLINE (CRITICAL): this fires ONLY on the USER-EDIT path. MangaEditedEvent is
    // published exclusively by the 3-arg UpdateManga(..., triggerSeriesEdited: true) from the
    // MangaController PUT (Phase 10 Plan 10-07 / Option B). The metadata-REFRESH path publishes
    // MangaUpdatedEvent instead — and an automatic refresh must NEVER delete files. We therefore
    // deliberately do NOT implement IHandle<MangaUpdatedEvent>. Do not add it.
    //
    // DESTRUCTIVE-BY-DESIGN: deletion routes through IDeleteMediaFiles.DeleteChapterFile, which
    // respects the RecycleBin when configured (recoverable). Deletion primitives are reused
    // VERBATIM from StrayChapterPruneService (files first, then rows).
    public sealed class MaxChapterCapEnforcementService : IHandle<MangaEditedEvent>
    {
        private readonly IChapterService _chapterService;
        private readonly IChapterFileService _chapterFileService;
        private readonly IDeleteMediaFiles _deleteMediaFiles;
        private readonly Logger _logger;

        public MaxChapterCapEnforcementService(
            IChapterService chapterService,
            IChapterFileService chapterFileService,
            IDeleteMediaFiles deleteMediaFiles,
            Logger logger)
        {
            _chapterService = chapterService;
            _chapterFileService = chapterFileService;
            _deleteMediaFiles = deleteMediaFiles;
            _logger = logger;
        }

        public void Handle(MangaEditedEvent message)
        {
            var manga = message.Manga;

            // No cap set (0 was already normalized to null in Manga.ApplyChanges) → no-op.
            if (!manga.MaxChapterNumber.HasValue)
            {
                return;
            }

            // Mirror the synthesis clamp + StrayChapterPruneService: clamp a negative baseline to
            // 0 first, then the effective ceiling is the LARGER of (user cap, metadata count). The
            // metadata count is the floor below which we must NEVER delete — a stale undercount
            // can't take out real downloaded extensions.
            var cap = manga.MaxChapterNumber.Value;
            if (cap < 0)
            {
                cap = 0;
            }

            var baseline = manga.TotalChapterCount.GetValueOrDefault();
            if (baseline < 0)
            {
                baseline = 0;
            }

            var ceiling = Math.Max(cap, baseline);

            // ChapterNumber is DECIMAL — a fractional like 38.5 > 38 is correctly above the
            // ceiling and IS deleted.
            var strays = _chapterService.GetChaptersByManga(manga.Id)
                .Where(c => c.ChapterNumber > (decimal)ceiling)
                .ToList();

            if (strays.Count == 0)
            {
                return;
            }

            // Files-by-chapter map (same shape as StrayChapterPruneService.Analyze).
            var filesByChapter = _chapterFileService.GetFilesByManga(manga.Id)
                .Where(f => f.ChapterId > 0)
                .GroupBy(f => f.ChapterId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Reuse StrayChapterPruneService deletion ORDERING verbatim: physical files FIRST
            // (recycle-bin-respecting), then drop the now-orphaned Chapter rows.
            var deletedFileCount = 0;

            foreach (var stray in strays)
            {
                if (!filesByChapter.TryGetValue(stray.Id, out var files))
                {
                    continue;
                }

                foreach (var fileRef in files)
                {
                    var file = _chapterFileService.Get(fileRef.Id);
                    if (file == null)
                    {
                        continue;
                    }

                    _deleteMediaFiles.DeleteChapterFile(manga, file);
                    deletedFileCount++;
                }
            }

            _chapterService.DeleteMany(strays);

            _logger.Info(
                "Max-chapter cap enforcement for manga '{0}' (id={1}, cap={2}, baseline={3}, "
                + "effective ceiling={4}): deleted {5} chapter row(s) above the ceiling, {6} file(s) "
                + "recycle-binned.",
                manga.Title,
                manga.Id,
                cap,
                baseline,
                ceiling,
                strays.Count,
                deletedFileCount);
        }
    }
}
