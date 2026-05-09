using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.MediaFiles.MangaImport;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.MediaFiles
{
    using Manga = NzbDrone.Core.Manga.Manga;

    // Sonarr divergence: NEW manga sibling per Phase 9 D-09-03 #1 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/MediaFiles/DiskScanService.cs.
    //
    // IExecute<RescanMangaCommand> + filesystem walk over Manga.Path (.cbz/.cbr/.zip/.cb7
    // per MangaFileExtensions). Calls MangaFileTableCleanupService at end (D-09-03 #1 lock).
    // OMITS the media-info updater dep per D-09-04 (UpdateChapterInfoService deferred to v1.1).
    //
    // Manga divergence vs TV DiskScanService:
    //   * IMakeMangaImportDecision.GetImportDecisions takes List<LocalChapter>, not List<string>
    //     (TV's IMakeImportDecision performs its own parse-and-resolve internally; manga puts
    //     that in the caller — matches existing ManualImportService.ProcessFolder pattern).
    //     Therefore Scan() builds LocalChapter objects via IMangaParsingService.Map +
    //     MangaParser.ParseChapterTitle before calling the decision maker.
    //   * No SeriesScanSkippedEvent manga sibling — log + return when root-folder is missing
    //     (per RESEARCH note: skip-event has no manga peer in v1).
    //   * No update-existing-files-with-different-size loop (TV uses the media-info updater
    //     for the post-scan re-probe path; manga drops this per D-09-04).
    //   * No GetNonVideoFiles / extra-file scan (manga has no Extras/ subtree per
    //     gap_deferred — see CONTEXT D-09-14).
    //
    // Phase 14 cleanup: collapse with DiskScanService when Tv/ deletes.
    public class MangaDiskScanService :
        IMangaDiskScanService,
        IExecute<RescanMangaCommand>
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IMakeMangaImportDecision _importDecisionMaker;
        private readonly IImportApprovedChapters _importApprovedChapters;
        private readonly IConfigService _configService;
        private readonly IMangaService _mangaService;
        private readonly IChapterFileService _chapterFileService;
        private readonly IChapterService _chapterService;
        private readonly IChapterReleaseService _chapterReleaseService;
        private readonly IMangaParsingService _parsingService;
        private readonly IMangaFileTableCleanupService _chapterFileTableCleanupService;
        private readonly IRootFolderService _rootFolderService;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public MangaDiskScanService(IDiskProvider diskProvider,
                                    IMakeMangaImportDecision importDecisionMaker,
                                    IImportApprovedChapters importApprovedChapters,
                                    IConfigService configService,
                                    IMangaService mangaService,
                                    IChapterFileService chapterFileService,
                                    IChapterService chapterService,
                                    IChapterReleaseService chapterReleaseService,
                                    IMangaParsingService parsingService,
                                    IMangaFileTableCleanupService chapterFileTableCleanupService,
                                    IRootFolderService rootFolderService,
                                    IEventAggregator eventAggregator,
                                    Logger logger)
        {
            _diskProvider = diskProvider;
            _importDecisionMaker = importDecisionMaker;
            _importApprovedChapters = importApprovedChapters;
            _configService = configService;
            _mangaService = mangaService;
            _chapterFileService = chapterFileService;
            _chapterService = chapterService;
            _chapterReleaseService = chapterReleaseService;
            _parsingService = parsingService;
            _chapterFileTableCleanupService = chapterFileTableCleanupService;
            _rootFolderService = rootFolderService;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        // Hidden-folder + thumbnail filter — manga has no Extras/ subtree (gap_deferred), but
        // these patterns are media-agnostic. Mirrors DiskScanService.ExcludedSubFoldersRegex
        // verbatim (drops extras/extrafanart/scenes since manga can't have those).
        private static readonly Regex ExcludedSubFoldersRegex = new Regex(@"(?:\\|\/|^)(?:@eadir|\.@__thumb|plex versions|\.[^\\/]+)(?:\\|\/)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ExcludedFilesRegex = new Regex(@"^\.(_|unmanic|DS_Store$)|^Thumbs\.db$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public void Scan(Manga manga)
        {
            var rootFolder = _rootFolderService.GetBestRootFolderPath(manga.Path);

            var mangaFolderExists = _diskProvider.FolderExists(manga.Path);

            if (!mangaFolderExists)
            {
                if (!_diskProvider.FolderExists(rootFolder))
                {
                    _logger.Warn("Manga's root folder ({0}) doesn't exist.", rootFolder);

                    // No MangaScanSkippedEvent sibling — log and return.
                    return;
                }

                if (_diskProvider.FolderEmpty(rootFolder))
                {
                    _logger.Warn("Manga's root folder ({0}) is empty.", rootFolder);

                    // No MangaScanSkippedEvent sibling — log and return.
                    return;
                }
            }

            _logger.ProgressInfo("Scanning {0}", manga.Title);

            if (!mangaFolderExists)
            {
                if (_configService.CreateEmptySeriesFolders)
                {
                    if (_configService.DeleteEmptyFolders)
                    {
                        _logger.Debug("Not creating missing manga folder: {0} because delete empty folders is enabled", manga.Path);
                    }
                    else
                    {
                        _logger.Debug("Creating missing manga folder: {0}", manga.Path);

                        _diskProvider.CreateFolder(manga.Path);
                        SetPermissions(manga.Path);
                    }
                }
                else
                {
                    _logger.Debug("Manga folder doesn't exist: {0}", manga.Path);
                }

                CleanMediaFiles(manga, new List<string>());
                CompletedScanning(manga);

                return;
            }

            var mangaFilesStopwatch = Stopwatch.StartNew();
            var mediaFileList = FilterPaths(manga.Path, GetMangaFiles(manga.Path)).ToList();
            mangaFilesStopwatch.Stop();
            _logger.Trace("Finished getting chapter files for: {0} [{1}]", manga, mangaFilesStopwatch.Elapsed);

            // D-09-03 #1: TableCleanup INSIDE the scan flow — TV mirror at DiskScanService.cs:134.
            CleanMediaFiles(manga, mediaFileList);

            var mangaFiles = _chapterFileService.GetFilesByManga(manga.Id);
            var unmappedFiles = ChapterFileService.FilterExistingFiles(mediaFileList, mangaFiles, manga);

            // Build LocalChapter aggregates from the unmapped paths so the manga import-decision
            // maker (which consumes List<LocalChapter>) can run. Mirrors ManualImportService's
            // ProcessFolder build-then-decide pattern.
            var existingChapters = _chapterService.GetChaptersByManga(manga.Id);
            var localChapters = new List<LocalChapter>();
            foreach (var file in unmappedFiles)
            {
                var lc = BuildLocalChapter(file, manga, existingChapters);
                if (lc != null)
                {
                    localChapters.Add(lc);
                }
            }

            // Issue #30 — surface the unmatched-file case at Warn level. The previous silent
            // skip downstream of `lc.Chapter == null` (inside ImportApprovedChapters.Import) hid
            // misnamed CBZ files / language-rank mismatches behind a "Completed scanning"
            // success log. Now the user sees one Warn line per unresolved file at scan time.
            foreach (var lc in localChapters.Where(lc => lc.Chapter == null))
            {
                _logger.Warn(
                    "Could not match file '{0}' to any chapter for {1}. " +
                    "Verify the filename includes a chapter number (e.g. '{1} - Chapter 001.cbz') and " +
                    "that a corresponding chapter row exists in the database.",
                    lc.Path,
                    manga.Title);
            }

            var decisionsStopwatch = Stopwatch.StartNew();
            var decisions = _importDecisionMaker.GetImportDecisions(localChapters, downloadClientItem: null);
            decisionsStopwatch.Stop();
            _logger.Trace("Import decisions complete for: {0} [{1}]", manga, decisionsStopwatch.Elapsed);
            _importApprovedChapters.Import(decisions, newDownload: false);

            // NO UpdateMediaInfo loop — D-09-04 deferral.

            RemoveEmptyMangaFolder(manga.Path);

            CompletedScanning(manga);
        }

        private LocalChapter BuildLocalChapter(string file, Manga manga, IList<Chapter> existingChapters)
        {
            try
            {
                var parsed = MangaParser.ParseChapterTitle(Path.GetFileNameWithoutExtension(file));
                var remoteChapter = _parsingService.Map(parsed, manga, existingChapters);
                var chapter = remoteChapter?.Chapters?.FirstOrDefault();

                // Issue #30 — when neither the parser nor the indexer supplies a language
                // tag (e.g., a CBZ file dropped into the manga's library folder named
                // `<Title> - Chapter NNN.cbz` with no language marker), back-propagate
                // from the matched Chapter's ChapterRelease rows. Phase 16 STRUCT-04
                // lifted language to ChapterRelease — pick the first ChapterRelease's
                // TranslatedLanguage as a best-effort fallback. When parser DOES supply
                // a language, it wins (consistent with MangaParsingService D-10 contract).
                var resolvedLanguage = parsed?.TranslatedLanguage
                    ?? (chapter != null
                        ? _chapterReleaseService.GetReleasesByChapter(chapter.Id).FirstOrDefault()?.TranslatedLanguage
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
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to build LocalChapter for: {0}", file);
                return null;
            }
        }

        private void CleanMediaFiles(Manga manga, List<string> mediaFileList)
        {
            _logger.Debug("{0} Cleaning up media files in DB", manga);
            _chapterFileTableCleanupService.Clean(manga, mediaFileList);
        }

        private void CompletedScanning(Manga manga)
        {
            _logger.Info("Completed scanning manga: {0}", manga.Title);

            // Pitfall 4 — event publish is the LAST line of the scan flow.
            _eventAggregator.PublishEvent(new MangaScannedEvent(manga, new List<string>()));
        }

        public string[] GetMangaFiles(string path, bool allDirectories = true)
        {
            _logger.Debug("Scanning '{0}' for manga archive files", path);

            var filesOnDisk = _diskProvider.GetFiles(path, allDirectories).ToList();

            var mediaFileList = filesOnDisk
                .Where(file => MangaFileExtensions.Extensions.Contains(Path.GetExtension(file)))
                .ToList();

            _logger.Trace("{0} files were found in {1}", filesOnDisk.Count, path);
            _logger.Debug("{0} manga archive files were found in {1}", mediaFileList.Count, path);

            return mediaFileList.ToArray();
        }

        public List<string> FilterPaths(string basePath, IEnumerable<string> files, bool filterExtras = true)
        {
            // Mirror DiskScanService.FilterPaths shape — manga has no extras/extrafanart filter,
            // but the hidden-folder + thumbnail patterns are reused. The filterExtras parameter
            // is preserved for signature compatibility with future manga-extras work.
            return files
                .Where(file => !ExcludedSubFoldersRegex.IsMatch(basePath.GetRelativePath(file)))
                .Where(file => !ExcludedFilesRegex.IsMatch(Path.GetFileName(file)))
                .ToList();
        }

        private void SetPermissions(string path)
        {
            if (!_configService.SetPermissionsLinux)
            {
                return;
            }

            try
            {
                _diskProvider.SetPermissions(path, _configService.ChmodFolder, _configService.ChownGroup);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to apply permissions to: " + path);
                _logger.Debug(ex, ex.Message);
            }
        }

        private void RemoveEmptyMangaFolder(string path)
        {
            if (_configService.DeleteEmptyFolders)
            {
                _diskProvider.RemoveEmptySubfolders(path);

                if (_diskProvider.FolderEmpty(path))
                {
                    _diskProvider.DeleteFolder(path, true);
                }
            }
        }

        public void Execute(RescanMangaCommand message)
        {
            if (message.MangaId.HasValue)
            {
                var manga = _mangaService.GetManga(message.MangaId.Value);
                Scan(manga);
            }
            else
            {
                var allManga = _mangaService.GetAllManga();

                foreach (var manga in allManga)
                {
                    Scan(manga);
                }
            }
        }
    }
}
