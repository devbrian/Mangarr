using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Core.MediaFiles;

namespace NzbDrone.Core.Manga
{
    // Sonarr divergence: NEW-in-Mangarr maintenance surface — no TV analog. Companion cleanup
    // for the ChapterSynthesisService stray-outlier guard: the density-floor cut PREVENTS new
    // phantom chapters, but a manga that was reconciled BEFORE the fix already carries the
    // contiguous backfill (e.g. chapters 87..726 materialized from a handful of mislabeled
    // gateway releases on an 86-chapter title). Once materialized, those file-less rows look
    // contiguous, so the FILE-LESS density signal can no longer spot them. This service uses the
    // orthogonal anchor the audit script (scripts/audit-stray-chapters.py) proved out: the
    // density cut measured ONLY over WITH-FILE evidence (the numbers that actually exist on disk
    // above the baseline) via the shared ChapterDensityCut.Resolve boundary.
    //
    //   * Rows AT/BELOW the cut    -> a DENSE real extension past a stale metadata count
    //                                 (metadata says 10, disk has 11..100) — NEVER pruned, only
    //                                 reported as LegitExtension. (This is the bug the density
    //                                 anchor fixes: the old crude "> metadata count" signal would
    //                                 have deleted these real chapters.)
    //   * file-less ABOVE the cut  -> CONFIDENT junk, but ONLY when on-disk evidence anchored the
    //                                 cut. Safe to delete (no artifact to lose; re-synthesizes via
    //                                 the FIXED density cut if it was genuinely real).
    //   * file-less, NO disk anchor-> UNCERTAIN. With zero with-file rows above the baseline the
    //                                 cut falls back to the baseline, so a sparse file-less set
    //                                 could be phantoms OR legitimately-wanted-but-ungrabbed
    //                                 chapters — a gateway search decides. NEVER auto-deleted;
    //                                 only pruned on an explicit pruneUncertain opt-in.
    //   * strays WITH a ChapterFile-> a mislabeled release may actually be REAL content, so these
    //                                 are NEVER auto-deleted — reported for review and only
    //                                 recycle-binned (recoverable) when deleteFiles=true.
    // Dry-run is the default everywhere; deletion is a separate, explicit call.
    public sealed class StrayChapterPruneService : IStrayChapterPruneService
    {
        private readonly IMangaService _mangaService;
        private readonly IChapterService _chapterService;
        private readonly IChapterFileService _chapterFileService;
        private readonly IDeleteMediaFiles _deleteMediaFiles;
        private readonly Logger _logger;

        public StrayChapterPruneService(
            IMangaService mangaService,
            IChapterService chapterService,
            IChapterFileService chapterFileService,
            IDeleteMediaFiles deleteMediaFiles,
            Logger logger)
        {
            _mangaService = mangaService;
            _chapterService = chapterService;
            _chapterFileService = chapterFileService;
            _deleteMediaFiles = deleteMediaFiles;
            _logger = logger;
        }

        public StrayChapterPruneReport BuildReport(int mangaId)
        {
            var manga = _mangaService.GetManga(mangaId);
            return manga == null ? null : Analyze(manga, dryRun: true);
        }

        public StrayChapterPruneReport Prune(int mangaId, bool deleteFiles, bool pruneUncertain = false)
        {
            var manga = _mangaService.GetManga(mangaId);
            if (manga == null)
            {
                return null;
            }

            var report = Analyze(manga, dryRun: false);

            if (!report.BaselineKnown)
            {
                _logger.Warn(
                    "Stray-chapter prune skipped for manga '{0}' (id={1}) — no metadata chapter count, "
                    + "cannot determine the trusted baseline.",
                    manga.Title,
                    manga.Id);
                return report;
            }

            // (1) Confident file-less junk — above the cut AND anchored by on-disk evidence.
            // Delete the Chapter rows outright; nothing on disk to lose and a genuinely-real
            // number re-synthesizes through the fixed density cut on next search.
            var rowsToDelete = new List<StrayChapterInfo>(report.FileLessStrays);

            // (2) UNCERTAIN file-less rows — no on-disk evidence anchored the cut, so they could be
            // legitimately-wanted chapters. Skipped by default; only pruned on the explicit opt-in.
            if (pruneUncertain)
            {
                rowsToDelete.AddRange(report.UncertainStrays);
            }
            else if (report.UncertainStrays.Count > 0)
            {
                _logger.Info(
                    "Stray-chapter prune for manga '{0}' (id={1}): {2} file-less row(s) above the metadata "
                    + "baseline have NO on-disk evidence to anchor the density cut — left untouched (run a "
                    + "search to confirm they don't exist, then re-prune with pruneUncertain=true).",
                    manga.Title,
                    manga.Id,
                    report.UncertainStrays.Count);
            }

            if (rowsToDelete.Count > 0)
            {
                var ids = rowsToDelete.Select(s => s.ChapterId).ToList();
                var rows = _chapterService.GetChapters(ids);
                _chapterService.DeleteMany(rows);
                report.DeletedChapterRowCount += rows.Count;
            }

            // (3) Strays WITH a file — only when the caller explicitly opts in. Recycle-bin the
            // physical artifact (recoverable) then drop the now-orphaned Chapter row.
            if (deleteFiles && report.WithFileStrays.Count > 0)
            {
                foreach (var stray in report.WithFileStrays)
                {
                    foreach (var fileInfo in stray.Files)
                    {
                        var file = _chapterFileService.Get(fileInfo.ChapterFileId);
                        if (file == null)
                        {
                            continue;
                        }

                        _deleteMediaFiles.DeleteChapterFile(manga, file);
                        report.DeletedFileCount++;
                    }
                }

                var ids = report.WithFileStrays.Select(s => s.ChapterId).ToList();
                var rows = _chapterService.GetChapters(ids);
                _chapterService.DeleteMany(rows);
                report.DeletedChapterRowCount += rows.Count;
            }

            _logger.Info(
                "Stray-chapter prune for manga '{0}' (id={1}, baseline={2}, cut={3}): deleted {4} chapter "
                + "row(s), {5} file(s) recycle-binned ({6} with-file + {7} legit-extension + {8} uncertain "
                + "stray(s) left untouched).",
                manga.Title,
                manga.Id,
                report.MetadataChapterCount,
                report.DensityCut,
                report.DeletedChapterRowCount,
                report.DeletedFileCount,
                deleteFiles ? 0 : report.WithFileStrays.Count,
                report.LegitExtension.Count,
                pruneUncertain ? 0 : report.UncertainStrays.Count);

            return report;
        }

        private StrayChapterPruneReport Analyze(Manga manga, bool dryRun)
        {
            var report = new StrayChapterPruneReport
            {
                MangaId = manga.Id,
                MangaTitle = manga.Title,
                MetadataChapterCount = manga.TotalChapterCount,
                DryRun = dryRun,
            };

            var baseline = manga.TotalChapterCount.GetValueOrDefault();
            if (baseline <= 0)
            {
                // No trusted metadata count — cannot safely tell a stray from a real chapter.
                report.BaselineKnown = false;
                return report;
            }

            report.BaselineKnown = true;

            var filesByChapter = _chapterFileService.GetFilesByManga(manga.Id)
                .Where(f => f.ChapterId > 0)
                .GroupBy(f => f.ChapterId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var strays = _chapterService.GetChaptersByManga(manga.Id)
                .Where(c => c.ChapterNumber > baseline)
                .OrderBy(c => c.ChapterNumber)
                .ToList();

            // Anchor the density cut ON WHAT ACTUALLY EXISTS ON DISK above the baseline (with-file
            // rows), NOT the file-less phantoms — a pre-fix contiguous backfill makes the file-less
            // region look dense even when it is all junk. Numbers above the cut are the true
            // outliers; numbers at/below it are a legitimate dense extension past a stale count.
            var withFileNumbers = strays
                .Where(c => HasFile(c, filesByChapter))
                .Select(c => c.ChapterNumber)
                .ToHashSet();

            var candidateMax = withFileNumbers.Count > 0 ? withFileNumbers.Max() : baseline;
            report.DensityCut = ChapterDensityCut.Resolve(baseline, candidateMax, withFileNumbers);
            report.DiskEvidenceAboveBaseline = withFileNumbers.Count > 0;

            foreach (var chapter in strays)
            {
                filesByChapter.TryGetValue(chapter.Id, out var files);
                var hasFile = HasFile(chapter, filesByChapter);

                var info = new StrayChapterInfo
                {
                    ChapterId = chapter.Id,
                    ChapterNumber = chapter.ChapterNumber,
                    Monitored = chapter.Monitored,
                    Title = chapter.Title,
                    FirstReleaseDate = chapter.FirstReleaseDate,
                    ExternalId = chapter.ExternalId,
                };

                if (hasFile)
                {
                    foreach (var file in files ?? new List<ChapterFile>())
                    {
                        info.Files.Add(new StrayFileInfo
                        {
                            ChapterFileId = file.Id,
                            Path = Path.Combine(manga.Path ?? string.Empty, file.RelativePath ?? string.Empty),
                            Size = file.Size,
                        });
                    }
                }

                // At/below the cut: a dense real extension past a stale metadata count — spare it.
                if (chapter.ChapterNumber <= report.DensityCut)
                {
                    report.LegitExtension.Add(info);
                }
                else if (hasFile)
                {
                    report.WithFileStrays.Add(info);
                }
                else if (report.DiskEvidenceAboveBaseline)
                {
                    // Above the cut AND anchored by on-disk evidence — confident junk.
                    report.FileLessStrays.Add(info);
                }
                else
                {
                    // Above the baseline but nothing on disk anchors the cut — could be wanted-legit.
                    report.UncertainStrays.Add(info);
                }
            }

            return report;
        }

        private static bool HasFile(Chapter chapter, IReadOnlyDictionary<int, List<ChapterFile>> filesByChapter)
        {
            return chapter.ChapterFileId.HasValue
                || (filesByChapter.TryGetValue(chapter.Id, out var files) && files.Count > 0);
        }
    }
}
