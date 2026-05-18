using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Download.Clients.InProcess;   // IChapterDownloadStateRepository — Plan 09-14 fast-path
using NzbDrone.Core.Manga;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Parser.Manga;

namespace NzbDrone.Core.MediaFiles.MangaImport.Manual
{
    // Sonarr divergence: NEW manga sibling per Phase 8 parity audit (no-sibling/ManualImportService).
    // Role-match analog: src/NzbDrone.Core/MediaFiles/EpisodeImport/Manual/ManualImportService.cs.
    //
    // Closes the consumer half of the cluster-08 manual-import quartet (08-01 ImportMode +
    // 08-02 ManualImportFile + 08-03 ManualImportItem + 08-07 ManualImportCommand). Mirrors
    // TV ManualImportService's two responsibilities:
    //   1. Preview surface — `GetMediaFiles(folder, downloadId, mangaId, filterExistingFiles)`
    //      scans the supplied folder, parses each candidate file with MangaParser, resolves
    //      Manga/Chapter against the DB, runs MangaImportDecisionMaker, and returns
    //      ManualImportItem rows for the InteractiveImport modal.
    //   2. Command execution — `IExecute<ManualImportCommand>` handles the user-confirmed
    //      ManualImportFile selections by building LocalChapter aggregates and handing them
    //      to ImportApprovedChapters (Phase 6 PIPELINE-04 pipeline owner).
    //
    // Manga divergences from TV:
    //   * No IAggregationService (manga has no scene-numbering / episode-aggregation per
    //     MangaImportDecisionMaker's Phase 6 simplification).
    //   * No IMangaTrackedDownloadService analog (heavier sibling service deferred — see
    //     v1.1 follow-up below). Phase 9 Plan 09-14 closes the downloadId fast-path TODO via
    //     OPTION A (audit gap-06 close-out): wire IChapterDownloadStateRepository directly
    //     into the ctor so GetMediaFiles can back-resolve the original Manga + StagingPath
    //     when the caller supplies a non-null downloadId. The fast-path mirrors TV's
    //     ITrackedDownloadService.Find shape semantically (silent no-op when stale) but uses
    //     the lighter Phase 4 state-row lookup instead of introducing a new sibling service.
    //     v1.1 follow-up (NOT Plan 09-14): introduce IMangaTrackedDownloadService analog if
    //     multi-consumer pattern emerges (per audit gap-06 OPTION B notes — currently
    //     IChapterDownloadStateRepository covers all in-scope consumers).
    //   * No IDiskScanService manga peer in v1 — uses raw IDiskProvider + the manga
    //     archive-extension allowlist (mirrors MangaTitleNormalizer's allowlist convention).
    //     TODO Phase 8 follow-up: replace with IMangaDiskScanService when that ships.
    //   * No V5 controller wiring in this plan (deferred — see Plan 08-08 scope).
    //   * No frontend wiring in this plan (deferred).
    //
    // Phase 8 cleanup: collapse with TV ManualImportService when Tv/ deletes.
    public interface IManualImportService
    {
        List<ManualImportItem> GetMediaFiles(string folder, string downloadId, int? mangaId, bool filterExistingFiles);

        // Phase 25 Plan 25-02 (gap-05 closure) — bulk reprocess for the V5
        // POST /api/v5/manualimport endpoint. Each input row is re-routed through
        // the standard decision pipeline so user-supplied Manga/Chapter overrides
        // surface refreshed Rejections without persisting anything to disk.
        List<ManualImportItem> ReprocessItems(List<ManualImportFile> files);
    }

    public class ManualImportService : IExecute<ManualImportCommand>, IManualImportService
    {
        // Manga archive extensions — matches MangaParser's allowlist (Phase 2 Parser/Manga
        // CLAUDE.md). Path.GetExtension is unsafe for `Ch.1` filenames so we lowercase-compare
        // the trailing token explicitly.
        private static readonly HashSet<string> MangaArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".cbz", ".cbr", ".cb7", ".cbt", ".zip", ".rar", ".pdf", ".epub"
        };

        private readonly IDiskProvider _diskProvider;
        private readonly IMangaParsingService _parsingService;
        private readonly IMangaService _mangaService;
        private readonly IChapterService _chapterService;
        private readonly IChapterFileService _chapterFileService;                // Phase 16.1 — Issue #30 back-propagation against ChapterFile grain (canonical post-Phase-16.1)
        private readonly IMakeMangaImportDecision _importDecisionMaker;
        private readonly IImportApprovedChapters _importApprovedChapters;
        private readonly IChapterDownloadStateRepository _chapterDownloadStateRepository;   // NEW per Plan 09-14 (audit gap-06)
        private readonly Logger _logger;

        public ManualImportService(IDiskProvider diskProvider,
                                   IMangaParsingService parsingService,
                                   IMangaService mangaService,
                                   IChapterService chapterService,
                                   IChapterFileService chapterFileService,                  // Phase 16.1 — replaces IChapterReleaseService
                                   IMakeMangaImportDecision importDecisionMaker,
                                   IImportApprovedChapters importApprovedChapters,
                                   IChapterDownloadStateRepository chapterDownloadStateRepository,   // NEW Plan 09-14
                                   Logger logger)
        {
            _diskProvider = diskProvider;
            _parsingService = parsingService;
            _mangaService = mangaService;
            _chapterService = chapterService;
            _chapterFileService = chapterFileService;
            _importDecisionMaker = importDecisionMaker;
            _importApprovedChapters = importApprovedChapters;
            _chapterDownloadStateRepository = chapterDownloadStateRepository;   // NEW Plan 09-14
            _logger = logger;
        }

        public List<ManualImportItem> GetMediaFiles(string folder, string downloadId, int? mangaId, bool filterExistingFiles)
        {
            // Sibling of TV ManualImportService.GetMediaFiles(string path, ...). Manga
            // simplification: no rooted-on-Manga.Path overload (the Manga aggregate
            // replacement for that lives in IChapterFileService.GetFilesByManga when the
            // V5 InteractiveImport controller ships). The downloadId fast-path below
            // (Plan 09-14) closes the previously-deferred TV TrackedDownload-equivalent.

            // Phase 9 Plan 09-14 (sub-wave A 09-05 audit gap-06 close-out): downloadId fast-path.
            // When caller supplies a non-null downloadId from the InteractiveImport modal post-grab
            // reconciliation flow, back-resolve the original Manga + StagingPath via the Phase 4
            // ChapterDownloadState row. Mirrors TV ITrackedDownloadService.Find(downloadId) shape
            // semantically (silent no-op when stale). Stale downloadId or null state row → fall
            // through to the existing folder-fallback chain below (NOT an error path).
            if (downloadId.IsNotNullOrWhiteSpace())
            {
                var stateRow = _chapterDownloadStateRepository.FindByDownloadId(downloadId);
                if (stateRow != null)
                {
                    // Override folder with the tracked-download's StagingPath when present
                    // (StagingPath is the manga-side equivalent of TV's ImportItem.OutputPath —
                    // see ChapterDownloadState.StagingPath: "OutputPath after Status=Completed (D-11)").
                    if (stateRow.StagingPath.IsNotNullOrWhiteSpace())
                    {
                        folder = stateRow.StagingPath;
                    }

                    // Seed mangaId from the tracked download ONLY if caller didn't supply one.
                    // Caller's mangaId wins over stateRow.MangaId — the InteractiveImport modal
                    // user-pick path is an intentional manual override (the user picked a
                    // different Manga than what the download client grabbed for).
                    if (!mangaId.HasValue)
                    {
                        mangaId = stateRow.MangaId;
                    }
                }

                // stateRow == null → silent fast-path skip; fall through to folder-fallback.
            }

            if (folder.IsNullOrWhiteSpace())
            {
                return new List<ManualImportItem>();
            }

            // Two shapes: caller may supply a folder OR a single file path. Mirrors TV
            // ManualImportService.GetMediaFiles lines 130-141.
            if (!_diskProvider.FolderExists(folder))
            {
                if (!_diskProvider.FileExists(folder))
                {
                    return new List<ManualImportItem>();
                }

                var rootFolder = Path.GetDirectoryName(folder) ?? folder;
                var single = ProcessFile(rootFolder, rootFolder, folder, downloadId, mangaId);
                return single != null ? new List<ManualImportItem> { single } : new List<ManualImportItem>();
            }

            return ProcessFolder(folder, folder, downloadId, mangaId, filterExistingFiles);
        }

        public void Execute(ManualImportCommand message)
        {
            _logger.ProgressTrace("Manually importing {0} files using mode {1}", message.Files.Count, message.ImportMode);

            // Build LocalChapter aggregates from each user-confirmed ManualImportFile and
            // hand them to ImportApprovedChapters in a single batch. ImportApprovedChapters
            // owns the Pitfall 4 ordering invariant (move → DB commit → event publish), so
            // we MUST NOT short-circuit it here.
            var localChapters = new List<LocalChapter>();

            for (var i = 0; i < message.Files.Count; i++)
            {
                _logger.ProgressTrace("Processing file {0} of {1}", i + 1, message.Files.Count);

                var file = message.Files[i];
                var manga = _mangaService.GetManga(file.MangaId);

                if (manga == null)
                {
                    _logger.Warn("Manga {0} not found for manual import file '{1}'", file.MangaId, file.Path);
                    continue;
                }

                var chapters = file.ChapterIds != null && file.ChapterIds.Any()
                    ? _chapterService.GetChapters(file.ChapterIds)
                    : new List<Chapter>();

                var chapter = chapters.FirstOrDefault();
                if (chapter == null)
                {
                    _logger.Warn("No chapters selected for manual import file '{0}' (manga {1})", file.Path, manga.Title);
                    continue;
                }

                var existingFile = manga.Path.IsNotNullOrWhiteSpace() && manga.Path.IsParentPath(file.Path);
                var size = _diskProvider.GetFileSize(file.Path);

                var localChapter = new LocalChapter
                {
                    Path = file.Path,
                    Size = size,
                    Manga = manga,
                    Chapter = chapter,
                    Chapters = chapters,
                    ExistingFile = existingFile,
                    DownloadItem = file.DownloadId.IsNotNullOrWhiteSpace()
                        ? new DownloadClientItemInfo { DownloadId = file.DownloadId }
                        : null
                };

                localChapters.Add(localChapter);
            }

            if (!localChapters.Any())
            {
                _logger.ProgressTrace("Manual import had no resolvable files");
                return;
            }

            // Run the manga decision maker so user-driven manual imports flow through the
            // same spec gates as auto-imports (idempotency, free-space, etc.).
            var decisions = _importDecisionMaker.GetImportDecisions(localChapters, downloadClientItem: null);
            var importResults = _importApprovedChapters.Import(decisions, newDownload: true);

            var imported = importResults.Count(r => r.Result == MangaImportResultType.Imported);
            if (imported > 0)
            {
                _logger.ProgressTrace("Manually imported {0} files", imported);
            }
        }

        // Phase 25 Plan 25-02 (gap-05 closure per Phase 8 audit) — bulk reprocess
        // for the V5 POST /api/v5/manualimport endpoint. Iterates each input row,
        // resolves the user-supplied Manga + Chapter overrides, rebuilds a
        // LocalChapter, and runs the standard decision pipeline so the response
        // carries refreshed Rejections. No filesystem mutation: this is preview-only
        // (ImportApprovedChapters is NOT invoked here — Execute owns that on user
        // confirm). Pitfall 4 ordering invariant in ImportApprovedChapters.Import
        // is untouched by this method.
        //
        // Mirrors Sonarr V3 ManualImportService.ReprocessItems shape (per
        // 25-01-PORT-SOURCE.md §3 + 25-PATTERNS.md lines 344-361). Folder-type
        // dispatch (ROOT / STAGING / ARBITRARY) is left to the existing
        // GetMediaFiles entry-point; ReprocessItems operates on user-edited rows
        // that already came back from a prior GetMediaFiles preview, so we
        // re-route through the same internal helpers without a new registry.
        public List<ManualImportItem> ReprocessItems(List<ManualImportFile> files)
        {
            files ??= new List<ManualImportFile>();
            _logger.Debug("Reprocessing {Count} ManualImportFile rows", files.Count);

            var results = new List<ManualImportItem>();

            foreach (var file in files)
            {
                results.Add(ReprocessSingle(file));
            }

            return results;
        }

        private ManualImportItem ReprocessSingle(ManualImportFile file)
        {
            // Resolve the user-supplied Manga override (MangaId > 0). When the
            // override is absent we fall back to parser-by-folder-name (mirrors
            // ProcessFolder line 240-248). The decision maker still surfaces
            // appropriate Rejections downstream if the parse fails.
            Manga.Manga manga = null;

            if (file.MangaId > 0)
            {
                manga = _mangaService.GetManga(file.MangaId);
            }
            else if (file.FolderName.IsNotNullOrWhiteSpace())
            {
                try
                {
                    manga = _parsingService.GetManga(file.FolderName);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Unable to resolve manga from folder name '{0}' during reprocess", file.FolderName);
                }
            }

            if (manga == null)
            {
                return new ManualImportItem
                {
                    Path = file.Path,
                    Name = file.Path != null ? Path.GetFileNameWithoutExtension(file.Path) : null,
                    FolderName = file.FolderName,
                    Size = file.Path != null && _diskProvider.FileExists(file.Path)
                        ? _diskProvider.GetFileSize(file.Path)
                        : 0,
                    DownloadId = file.DownloadId,
                    ChapterFileId = file.ChapterFileId,
                    ScanlationGroup = file.ScanlationGroup,
                    IndexerFlags = file.IndexerFlags,
                    ReleaseType = file.ReleaseType,
                    Rejections = new List<MangaImportRejection>
                    {
                        new MangaImportRejection(ImportRejectionReason.Unknown, "Unknown Manga")
                    }
                };
            }

            // Resolve user-supplied chapter overrides; when ChapterIds is empty the
            // BuildLocalChapter helper parses the filename and lets the decision
            // pipeline rejection-surface any un-resolved-chapter issue.
            var existingChapters = _chapterService.GetChaptersByManga(manga.Id);
            var lc = BuildLocalChapter(file.Path, manga, existingChapters);

            if (lc == null)
            {
                return new ManualImportItem
                {
                    Path = file.Path,
                    Name = file.Path != null ? Path.GetFileNameWithoutExtension(file.Path) : null,
                    FolderName = file.FolderName,
                    Size = file.Path != null && _diskProvider.FileExists(file.Path)
                        ? _diskProvider.GetFileSize(file.Path)
                        : 0,
                    DownloadId = file.DownloadId,
                    Manga = manga,
                    ChapterFileId = file.ChapterFileId,
                    ScanlationGroup = file.ScanlationGroup,
                    IndexerFlags = file.IndexerFlags,
                    ReleaseType = file.ReleaseType,
                    Rejections = new List<MangaImportRejection>()
                };
            }

            // When the user explicitly overrode the chapter set, apply that on top
            // of the parser-inferred chapters so the decision pipeline operates on
            // the user-picked aggregate.
            if (file.ChapterIds != null && file.ChapterIds.Any())
            {
                var overriddenChapters = _chapterService.GetChapters(file.ChapterIds);
                lc.Chapters = overriddenChapters;
                lc.Chapter = overriddenChapters.FirstOrDefault();
            }

            // Per-row scanlation-group override (user picked a specific group on
            // the modal). When unset, fall back to whatever BuildLocalChapter
            // parsed off the filename.
            if (file.ScanlationGroup.IsNotNullOrWhiteSpace())
            {
                lc.ScanlationGroup = file.ScanlationGroup;
            }

            lc.DownloadItem = file.DownloadId.IsNotNullOrWhiteSpace()
                ? new DownloadClientItemInfo { DownloadId = file.DownloadId }
                : null;

            var decision = _importDecisionMaker.GetDecision(lc, downloadClientItem: null);

            return new ManualImportItem
            {
                Path = lc.Path,
                FolderName = file.FolderName,
                Name = lc.Path != null ? Path.GetFileName(lc.Path) : null,
                Size = lc.Size > 0 ? lc.Size : (lc.Path != null && _diskProvider.FileExists(lc.Path)
                    ? _diskProvider.GetFileSize(lc.Path)
                    : 0),
                DownloadId = file.DownloadId,
                Manga = lc.Manga,
                Chapters = lc.Chapters ?? new List<Chapter>(),
                ChapterFileId = lc.Chapter?.ChapterFileId ?? file.ChapterFileId,
                TranslatedLanguage = lc.TranslatedLanguage,
                ScanlationGroup = lc.ScanlationGroup,
                CustomFormats = lc.CustomFormats ?? new(),
                CustomFormatScore = lc.CustomFormatScore,
                IndexerFlags = file.IndexerFlags,
                ReleaseType = file.ReleaseType,
                Rejections = decision.Rejections
            };
        }

        private List<ManualImportItem> ProcessFolder(string rootFolder, string baseFolder, string downloadId, int? mangaId, bool filterExistingFiles)
        {
            Manga.Manga manga = null;
            var directoryInfo = new DirectoryInfo(baseFolder);

            if (mangaId.HasValue)
            {
                manga = _mangaService.GetManga(mangaId.Value);
            }
            else
            {
                try
                {
                    manga = _parsingService.GetManga(directoryInfo.Name);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Unable to find manga from folder name '{0}'", directoryInfo.Name);
                }
            }

            // Always enumerate files at this depth + recurse into subfolders so the user
            // sees per-file rows, even when the manga can't be auto-resolved.
            var files = ListMangaArchives(baseFolder);

            if (manga == null)
            {
                if (files.Count > 100)
                {
                    _logger.Warn("Unable to determine manga from folder name and found more than 100 files. Skipping parsing");
                    return ProcessDownloadDirectory(rootFolder, files);
                }

                var subfolders = _diskProvider.GetDirectories(baseFolder).ToList();
                var processedFiles = files.Select(file => ProcessFile(rootFolder, baseFolder, file, downloadId, null));
                var processedFolders = subfolders.SelectMany(subfolder =>
                    ProcessFolder(rootFolder, subfolder, downloadId, null, filterExistingFiles));

                return processedFiles.Concat(processedFolders).Where(i => i != null).ToList();
            }

            // Manga resolved — parse-and-decide each file in this folder. Recursion into
            // subfolders is intentionally dropped here (mirrors TV: once Series is known
            // the flat batch is what feeds the decision maker). Phase 8 follow-up may
            // revisit if real-world manga drops nest deeper than v1 expects.
            var existingChapters = _chapterService.GetChaptersByManga(manga.Id);
            var localChapters = new List<LocalChapter>();

            foreach (var file in files)
            {
                if (filterExistingFiles && manga.Path.IsNotNullOrWhiteSpace() && manga.Path.IsParentPath(file))
                {
                    continue;
                }

                var lc = BuildLocalChapter(file, manga, existingChapters);
                if (lc != null)
                {
                    localChapters.Add(lc);
                }
            }

            var decisions = _importDecisionMaker.GetImportDecisions(localChapters, downloadClientItem: null);
            return decisions.Select(d => MapItem(d, rootFolder, downloadId, directoryInfo.Name)).ToList();
        }

        private ManualImportItem ProcessFile(string rootFolder, string baseFolder, string file, string downloadId, int? mangaId)
        {
            try
            {
                Manga.Manga manga = null;

                if (mangaId.HasValue)
                {
                    manga = _mangaService.GetManga(mangaId.Value);
                }

                if (manga == null)
                {
                    var relativeFile = baseFolder.GetRelativePath(file);
                    manga = _parsingService.GetManga(relativeFile);
                }

                if (manga == null)
                {
                    manga = _parsingService.GetManga(Path.GetFileNameWithoutExtension(file));
                }

                if (manga == null)
                {
                    return new ManualImportItem
                    {
                        DownloadId = downloadId,
                        Path = file,
                        RelativePath = rootFolder.GetRelativePath(file),
                        Name = Path.GetFileNameWithoutExtension(file),
                        Size = _diskProvider.GetFileSize(file),
                        Rejections = new List<MangaImportRejection>
                        {
                            new MangaImportRejection(ImportRejectionReason.Unknown, "Unknown Manga")
                        }
                    };
                }

                var existingChapters = _chapterService.GetChaptersByManga(manga.Id);
                var lc = BuildLocalChapter(file, manga, existingChapters);

                if (lc == null)
                {
                    return new ManualImportItem
                    {
                        DownloadId = downloadId,
                        Path = file,
                        RelativePath = rootFolder.GetRelativePath(file),
                        Name = Path.GetFileNameWithoutExtension(file),
                        Size = _diskProvider.GetFileSize(file),
                        Rejections = new List<MangaImportRejection>()
                    };
                }

                var decision = _importDecisionMaker.GetDecision(lc, downloadClientItem: null);
                return MapItem(decision, rootFolder, downloadId, null);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to process file: {0}", file);
            }

            return new ManualImportItem
            {
                DownloadId = downloadId,
                Path = file,
                RelativePath = rootFolder.GetRelativePath(file),
                Name = Path.GetFileNameWithoutExtension(file),
                Size = _diskProvider.GetFileSize(file),
                Rejections = new List<MangaImportRejection>()
            };
        }

        private List<ManualImportItem> ProcessDownloadDirectory(string rootFolder, List<string> archiveFiles)
        {
            var items = new List<ManualImportItem>();

            foreach (var file in archiveFiles)
            {
                items.Add(new ManualImportItem
                {
                    Path = file,
                    RelativePath = rootFolder.GetRelativePath(file),
                    Name = Path.GetFileNameWithoutExtension(file),
                    Size = _diskProvider.GetFileSize(file),
                    Rejections = new List<MangaImportRejection>()
                });
            }

            return items;
        }

        private LocalChapter BuildLocalChapter(string file, Manga.Manga manga, IList<Chapter> existingChapters)
        {
            var parsed = MangaParser.ParseChapterTitle(Path.GetFileNameWithoutExtension(file));
            var remoteChapter = _parsingService.Map(parsed, manga, existingChapters);

            var chapter = remoteChapter?.Chapters?.FirstOrDefault();

            // Issue #30 — when neither the parser nor the indexer supplies a language tag
            // (e.g., a CBZ file dropped via manual-import with no language marker in the
            // name), back-propagate from the matched Chapter's existing ChapterFile.
            // Phase 16.1 — TranslatedLanguage is canonical on ChapterFile per D-06.
            // When parser DOES supply a language, it wins (consistent with
            // MangaParsingService D-10 contract).
            var resolvedLanguage = parsed?.TranslatedLanguage
                ?? (chapter != null
                    ? _chapterFileService.GetFilesByChapter(chapter.Id).FirstOrDefault()?.TranslatedLanguage
                    : null);

            return new LocalChapter
            {
                Path = file,
                Size = _diskProvider.GetFileSize(file),
                Manga = manga,
                Chapter = chapter,
                Chapters = remoteChapter?.Chapters ?? new List<Chapter>(),
                ParsedChapterInfo = parsed,
                TranslatedLanguage = resolvedLanguage,
                ScanlationGroup = parsed?.ScanlationGroup,
                ExistingFile = manga.Path.IsNotNullOrWhiteSpace() && manga.Path.IsParentPath(file)
            };
        }

        private List<string> ListMangaArchives(string folder)
        {
            // No IMangaDiskScanService peer yet (TODO Phase 8 follow-up). Use the raw disk
            // provider + manga archive extension allowlist. Mirrors the corpus-validated
            // Parser/Manga extension list (CBZ/CBR/CB7/CBT/ZIP/RAR/PDF/EPUB).
            var allFiles = _diskProvider.GetFiles(folder, false);
            return allFiles
                .Where(f =>
                {
                    var ext = Path.GetExtension(f);
                    return !string.IsNullOrWhiteSpace(ext) && MangaArchiveExtensions.Contains(ext);
                })
                .ToList();
        }

        private ManualImportItem MapItem(MangaImportDecision decision, string rootFolder, string downloadId, string folderName)
        {
            var lc = decision.LocalChapter;

            return new ManualImportItem
            {
                Path = lc.Path,
                FolderName = folderName,
                RelativePath = rootFolder.GetRelativePath(lc.Path),
                Name = Path.GetFileNameWithoutExtension(lc.Path),
                DownloadId = downloadId,
                Manga = lc.Manga,
                Chapters = lc.Chapters ?? new List<Chapter>(),
                ChapterFileId = lc.Chapter?.ChapterFileId,
                TranslatedLanguage = lc.TranslatedLanguage ?? lc.Release?.TranslatedLanguage,
                ScanlationGroup = lc.ScanlationGroup ?? lc.Release?.ScanlationGroup,
                Size = lc.Size > 0 ? lc.Size : _diskProvider.GetFileSize(lc.Path),
                CustomFormats = lc.CustomFormats ?? new(),
                CustomFormatScore = lc.CustomFormatScore,
                Rejections = decision.Rejections
            };
        }
    }
}
