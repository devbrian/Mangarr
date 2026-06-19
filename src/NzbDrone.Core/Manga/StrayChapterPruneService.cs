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
    // gateway releases on an 86-chapter title). Once materialized, those rows look contiguous,
    // so the density signal can no longer spot them — this service uses the orthogonal signal:
    // a file-less synthesized row ABOVE the trusted metadata baseline. Two-bucket safety:
    //   * file-less strays            -> safe to delete (no artifact to lose; re-synthesizes
    //                                    via the FIXED density cut if it was genuinely real);
    //   * strays WITH a ChapterFile   -> a mislabeled release may actually be REAL content, so
    //                                    these are NEVER auto-deleted — reported for review and
    //                                    only recycle-binned (recoverable) when deleteFiles=true.
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

        public StrayChapterPruneReport Prune(int mangaId, bool deleteFiles)
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

            // (1) File-less strays — delete the Chapter rows outright. Nothing on disk to lose;
            // a genuinely-real number re-synthesizes through the fixed density cut on next search.
            if (report.FileLessStrays.Count > 0)
            {
                var ids = report.FileLessStrays.Select(s => s.ChapterId).ToList();
                var rows = _chapterService.GetChapters(ids);
                _chapterService.DeleteMany(rows);
                report.DeletedChapterRowCount += rows.Count;
            }

            // (2) Strays WITH a file — only when the caller explicitly opts in. Recycle-bin the
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
                "Stray-chapter prune for manga '{0}' (id={1}, baseline={2}): deleted {3} chapter row(s), "
                + "{4} file(s) recycle-binned ({5} with-file stray(s) left untouched).",
                manga.Title,
                manga.Id,
                report.MetadataChapterCount,
                report.DeletedChapterRowCount,
                report.DeletedFileCount,
                deleteFiles ? 0 : report.WithFileStrays.Count);

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
                .OrderBy(c => c.ChapterNumber);

            foreach (var chapter in strays)
            {
                filesByChapter.TryGetValue(chapter.Id, out var files);
                var hasFile = chapter.ChapterFileId.HasValue || (files != null && files.Count > 0);

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

                    report.WithFileStrays.Add(info);
                }
                else
                {
                    report.FileLessStrays.Add(info);
                }
            }

            return report;
        }
    }
}
