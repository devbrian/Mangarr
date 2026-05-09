using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients.InProcess;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MediaFiles.Commands;

// Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — MediaFiles/EpisodeImport/ DELETED.
//   using NzbDrone.Core.MediaFiles.EpisodeImport; ← deleted
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Organizer.Manga;
using NzbDrone.Core.Profiles.Translations;

namespace NzbDrone.Core.MediaFiles.MangaImport
{
    // Sonarr divergence: NEW manga sibling per Phase 6 PIPELINE-04 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/MediaFiles/EpisodeImport/ImportApprovedEpisodes.cs.
    //
    // ============================================================================
    // PITFALL 4 ORDERING INVARIANT (Phase 6 RESEARCH §Pitfall 4):
    //
    //     Per-decision success-path order MUST be:
    //
    //       1. Build destination path via MangaPathBuilder.BuildChapterPath
    //       2. Move staging CBZ → library via IDiskProvider.MoveFile
    //       3. Build ChapterFile entity + _chapterFileService.Add(chapterFile)  ← DB COMMIT
    //       4. Update Chapter.ChapterFileId FK + _chapterService.UpdateChapter
    //       5. Delete Phase 4 ChapterDownloadState row (lifecycle hook)
    //       6. _eventAggregator.PublishEvent(new ChapterImportedEvent { ... })  ← LAST LINE
    //
    //     The PublishEvent call MUST be the LAST line in the success path. Komga/Kavita
    //     rescan handlers (Plans 06-10/11) fire on this event; if it publishes before
    //     the DB commit + filesystem move complete, the rescan finds no new file and
    //     reports "0 new files imported" — silently broken pipeline.
    // ============================================================================
    //
    // Manga divergences from TV ImportApprovedEpisodes:
    //   * Uses _chapterFileService.Add (not _mediaFileService.Add — that takes EpisodeFile).
    //   * Uses _pathBuilder.BuildChapterPath (Plan 06-01 deliverable, not BuildPath/BuildFilePath).
    //   * IUpgradeChapterFiles invocation per Phase 9 D-09-05: when a decision passes
    //     UpgradeSpecification AND the chapter has an existing ChapterFileId > 0, the
    //     previous CBZ is recycled via IRecycleBinProvider + the old ChapterFile row is
    //     deleted (Pitfall 4 ordering: recycle FIRST, delete row SECOND, new ChapterFile
    //     insert + ChapterImportedEvent publish LAST). See PATTERNS §A.
    //   * No IExtraService / IExistingExtraFiles (no manga subtitle/extras concept).
    //   * Deletes Phase 4 ChapterDownloadState row + scratch dir on success
    //     (Phase 4 D-08 lifecycle).
    //   * Emits ChapterImportFailedEvent (Phase 6 D-12) on RootFolderNotFoundException /
    //     RecycleBinException / generic exception (Pitfall 4 mitigation: failures still
    //     publish so the auto-retry orchestrator (Plan 06-08) sees them).
    //
    // Phase 8 cleanup: collapse with TV ImportApprovedEpisodes when Tv/ deletes.
    public class ImportApprovedChapters : IImportApprovedChapters
    {
        private readonly IChapterFileService _chapterFileService;
        private readonly IChapterService _chapterService;
        private readonly IDiskProvider _diskProvider;
        private readonly IBuildMangaPaths _pathBuilder;
        private readonly IChapterDownloadStateRepository _stateRepo;
        private readonly IEventAggregator _eventAggregator;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly ITranslationProfileService _translationProfileService;
        private readonly IConfigService _configService;
        private readonly IUpgradeChapterFiles _upgradeChapterFileService;   // Phase 9 D-09-05
        private readonly Logger _logger;

        public ImportApprovedChapters(
            IChapterFileService chapterFileService,
            IChapterService chapterService,
            IDiskProvider diskProvider,
            IBuildMangaPaths pathBuilder,
            IChapterDownloadStateRepository stateRepo,
            IEventAggregator eventAggregator,
            IManageCommandQueue commandQueueManager,
            ITranslationProfileService translationProfileService,
            IConfigService configService,
            IUpgradeChapterFiles upgradeChapterFileService,                  // Phase 9 D-09-05
            Logger logger)
        {
            _chapterFileService = chapterFileService;
            _chapterService = chapterService;
            _diskProvider = diskProvider;
            _pathBuilder = pathBuilder;
            _stateRepo = stateRepo;
            _eventAggregator = eventAggregator;
            _commandQueueManager = commandQueueManager;
            _translationProfileService = translationProfileService;
            _configService = configService;
            _upgradeChapterFileService = upgradeChapterFileService;
            _logger = logger;
        }

        public List<MangaImportResult> Import(
            List<MangaImportDecision> decisions,
            bool newDownload,
            DownloadClientItem downloadClientItem = null)
        {
            var importResults = new List<MangaImportResult>();

            // Phase 8 audit gap-02 — mirrors ImportApprovedEpisodes lines 59-65 + 69-70.
            // TV groups qualified imports by SeriesId, then within each group orders by
            // QualityModelComparer desc + Size desc — so the BEST candidate per (series,
            // episode) is processed first; the dedup check naturally keeps the best one.
            //
            // Manga port: group by MangaId, then within each group order by ChapterNumber asc
            // (chronological library order) + upgrade-rank desc (TranslationProfile language
            // rank → CustomFormat score → Size — mirrors MangaDownloadDecisionComparer D-08
            // ordering, restricted to the LocalChapter-shaped fields available pre-RemoteChapter).
            // The seenChapterIds dedup below then naturally keeps the BEST candidate per
            // chapter ID instead of insertion-order — closes the two-CBZ-same-chapter
            // regression where the worse-ranked file won 50% of the time.
            var upgradeRankComparer = new LocalChapterUpgradeRankComparer(_translationProfileService, _configService);
            var qualified = decisions
                .Where(d => d.Approved)
                .GroupBy(d => d.LocalChapter.Manga?.Id ?? 0)
                .SelectMany(group => group
                    .OrderBy(d => d.LocalChapter.Chapter?.ChapterNumber ?? decimal.MaxValue)
                    .ThenByDescending(d => d, upgradeRankComparer))
                .ToList();
            var seenChapterIds = new HashSet<int>();

            foreach (var decision in qualified)
            {
                var lc = decision.LocalChapter;

                try
                {
                    // ---- 0. In-batch dedupe — same chapter ID cannot import twice in one batch ----
                    if (lc.Chapter != null && !seenChapterIds.Add(lc.Chapter.Id))
                    {
                        importResults.Add(new MangaImportResult(decision, "Chapter has already been imported in this batch"));
                        continue;
                    }

                    if (lc.Manga == null || lc.Chapter == null)
                    {
                        // Issue #30 — surface the unmatched-LocalChapter case at Warn level so
                        // disk-scan / manual-import flows that silently dropped files (e.g. parser
                        // language drift vs DB chapter language) don't disappear into a string-only
                        // result that nobody reads. The result is added to importResults too so
                        // callers that DO consume the return value still see the rejection.
                        _logger.Warn(
                            "Skipping import of {0}: Manga or Chapter could not be resolved on LocalChapter (manga={1}, chapter={2}).",
                            lc.Path,
                            lc.Manga?.Title ?? "(null)",
                            lc.Chapter?.Id.ToString() ?? "(null)");
                        importResults.Add(new MangaImportResult(decision, "Manga or Chapter missing on LocalChapter — cannot import"));
                        continue;
                    }

                    // ---- 0.5 NEW per Phase 9 D-09-05 — Upgrade promotion: recycle previous file BEFORE destination build ----
                    // Mirrors TV ImportApprovedEpisodes upgrade-call site. Pitfall 4 ordering preserved
                    // (recycle + DB-delete old row happen BEFORE new ChapterFile row insert + ChapterImportedEvent
                    // publish at step 6). The presence of a prior ChapterFileId means the decision passed
                    // UpgradeSpecification (Phase 6 D-10 three-state effective-upgrade-allowed gate).
                    //
                    // First arg is null (recycle-only mode): step 2 below already owns the new-file move via
                    // _diskProvider.MoveFile, so UpgradeChapterFileService skips its mover invocation and
                    // only performs the recycle + delete-row side effect documented above.
                    if (lc.Chapter.ChapterFileId.HasValue && lc.Chapter.ChapterFileId.Value > 0)
                    {
                        try
                        {
                            _upgradeChapterFileService.UpgradeChapterFile(null, lc, copyOnly: false);
                        }
                        catch (Exception upgradeEx)
                        {
                            // Recycle failure is non-fatal in TV (UpgradeMediaFileService.cs same shape):
                            // the new file still lands; the old leaks to disk but at least the import
                            // doesn't fail outright. Diagnostic surfaces in logs; downstream rescan can
                            // reconcile. We deliberately do NOT publish ChapterImportFailedEvent here —
                            // surfacing as failure would block the auto-retry orchestrator from finishing
                            // an otherwise-successful upgrade import.
                            _logger.Error(upgradeEx, "Failed to recycle previous ChapterFile during upgrade for chapter {0} of manga {1}", lc.Chapter.Id, lc.Manga?.Title);
                        }
                    }

                    // ---- 1. Build destination path (Phase 5 builder + Plan 06-01 BuildChapterPath) ----
                    var destinationPath = _pathBuilder.BuildChapterPath(lc.Manga, lc.Chapter, lc.Path);

                    // ---- 2. Move staging CBZ → library ----
                    var destinationDir = Path.GetDirectoryName(destinationPath);
                    if (destinationDir.IsNotNullOrWhiteSpace())
                    {
                        _diskProvider.EnsureFolder(destinationDir);
                    }

                    if (newDownload && !lc.ExistingFile)
                    {
                        if (!_diskProvider.FileExists(lc.Path))
                        {
                            throw new FileNotFoundException("Staging CBZ missing", lc.Path);
                        }

                        _diskProvider.MoveFile(lc.Path, destinationPath);
                    }

                    // ---- 3. Build ChapterFile + DB write FIRST (before event publish) ----
                    var actualPath = _diskProvider.FileExists(destinationPath) ? destinationPath : lc.Path;
                    var chapterFile = new ChapterFile
                    {
                        MangaId = lc.Manga.Id,
                        ChapterId = lc.Chapter.Id,
                        Path = actualPath,
                        RelativePath = ToRelativePath(lc.Manga.Path, actualPath),

                        // Phase 6 Plan 14 — BL-04 mitigation. lc.Size is the authoritative
                        // file size from Phase 4 staging (already consumed by
                        // FreeSpaceSpecification + NotEmptyArchiveSpecification, so it is
                        // reliable when > 0). Falling back to SafeGetFileSize only when
                        // upstream did not populate it avoids silent ChapterFile.Size = 0
                        // corruption on transient post-move I/O failures (Windows + AV +
                        // network shares). Downstream ChapterHistory + ChapterImportMessage
                        // carry this value forward — Size = 0 propagates forever.
                        Size = lc.Size > 0 ? lc.Size : SafeGetFileSize(actualPath, _logger),
                        DateAdded = DateTime.UtcNow,
                        OriginalFilePath = lc.Path,
                        TranslatedLanguage = lc.TranslatedLanguage ?? lc.Release?.TranslatedLanguage,
                        ScanlationGroup = lc.ScanlationGroup ?? lc.Release?.ScanlationGroup,
                        ReleaseGroup = lc.Release?.Indexer
                    };

                    chapterFile = _chapterFileService.Add(chapterFile);

                    // ---- 4. Wire Chapter.ChapterFileId FK (Plan 06-01 PIPELINE-04 column) ----
                    lc.Chapter.ChapterFileId = chapterFile.Id;
                    _chapterService.UpdateChapter(lc.Chapter);

                    // ---- 5. Delete Phase 4 ChapterDownloadState row (idempotent — Plan 06-01) ----
                    _stateRepo.DeleteByChapterId(lc.Chapter.Id);

                    // ---- 6. PITFALL 4 GUARD — PublishEvent is the LAST line in the success path ----
                    // Notification fan-out (Komga/Kavita rescan) can race-fire as soon as this lands;
                    // the file move + DB commit above MUST have completed before this point.
                    _eventAggregator.PublishEvent(new ChapterImportedEvent
                    {
                        Manga = lc.Manga,
                        Chapter = lc.Chapter,
                        ChapterFile = chapterFile,
                        DownloadClientItem = downloadClientItem,
                        NewDownload = newDownload,
                        SourcePath = lc.Path
                    });

                    importResults.Add(new MangaImportResult(decision, chapterFile));
                }
                catch (RootFolderNotFoundException ex)
                {
                    _logger.Warn(ex, "Root folder missing for {0}", lc.Manga?.Title);
                    _eventAggregator.PublishEvent(new ChapterImportFailedEvent
                    {
                        Manga = lc.Manga,
                        Chapter = lc.Chapter,
                        SourcePath = lc.Path,
                        FailureReason = ex.Message,
                        DownloadClientItem = downloadClientItem
                    });
                    importResults.Add(new MangaImportResult(decision, $"Root folder missing: {ex.Message}"));
                }
                catch (DestinationAlreadyExistsException ex)
                {
                    // Phase 8 audit gap-01 — mirrors ImportApprovedEpisodes lines 181-187.
                    // Two-source race: a chapter file already lives at the destination
                    // (e.g., user manually dropped the CBZ while auto-import was running).
                    // Log Warn, surface a Rejected import result, and queue a
                    // RescanMangaCommand so a future disk-scan reconciles the orphan
                    // file into the DB instead of the manga showing as "missing chapter"
                    // forever. NB: no IExecute<RescanMangaCommand> handler exists yet
                    // (deferred to follow-up plan); the command is queued and silently
                    // dropped until the handler ships. The reject result + Warn log
                    // remain valuable diagnostics in the meantime.
                    _logger.Warn(ex, "Couldn't import chapter {0}", lc.Chapter?.ChapterNumber);
                    importResults.Add(new MangaImportResult(decision, $"Failed to import chapter, Destination already exists: {ex.Message}"));

                    if (lc.Manga != null)
                    {
                        _commandQueueManager.Push(new RescanMangaCommand(lc.Manga.Id));
                    }
                }
                catch (RecycleBinException ex)
                {
                    _logger.Warn(ex, "Recycle bin failure importing chapter at {0}", lc.Path);
                    _eventAggregator.PublishEvent(new ChapterImportFailedEvent
                    {
                        Manga = lc.Manga,
                        Chapter = lc.Chapter,
                        SourcePath = lc.Path,
                        FailureReason = ex.Message,
                        DownloadClientItem = downloadClientItem
                    });
                    importResults.Add(new MangaImportResult(decision, $"Recycle bin failure: {ex.Message}"));
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to import chapter at {0}", lc.Path);
                    _eventAggregator.PublishEvent(new ChapterImportFailedEvent
                    {
                        Manga = lc.Manga,
                        Chapter = lc.Chapter,
                        SourcePath = lc.Path,
                        FailureReason = ex.Message,
                        DownloadClientItem = downloadClientItem
                    });
                    importResults.Add(new MangaImportResult(decision, ex.Message));
                }
            }

            // Trailing: surface rejected decisions to the caller too (mirrors TV
            // ImportApprovedEpisodes lines 202-204 — caller treats Result.Rejected uniformly).
            importResults.AddRange(decisions
                .Where(d => !d.Approved)
                .Select(d => new MangaImportResult(d, d.Rejections.Select(r => r.Message).ToArray())));

            return importResults;
        }

        // Phase 6 Plan 14 — BL-04 mitigation. Returns 0 on failure (return contract
        // unchanged so existing callers are unaffected), but logs the swallowed exception
        // at Warn level so transient I/O failures surface in diagnostics rather than
        // disappearing silently into the void.
        private static long SafeGetFileSize(string path, Logger logger)
        {
            try
            {
                return new FileInfo(path).Length;
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "SafeGetFileSize failed for path '{0}'; falling back to 0. ChapterFile may carry incorrect Size.", path);
                return 0;
            }
        }

        private static string ToRelativePath(string mangaPath, string filePath)
        {
            if (mangaPath.IsNullOrWhiteSpace() || filePath.IsNullOrWhiteSpace())
            {
                return Path.GetFileName(filePath ?? string.Empty);
            }

            if (mangaPath.IsParentPath(filePath))
            {
                return mangaPath.GetRelativePath(filePath);
            }

            return Path.GetFileName(filePath);
        }

        // Phase 8 audit gap-02 — bridges the public MangaDownloadDecisionComparer (which
        // operates on MangaDownloadDecision / RemoteChapter) into the LocalChapter shape
        // ImportApprovedChapters sees. Mirrors the upgrade-relevant slice of D-08 ordering:
        // language rank (lower index in TranslationProfile.Languages = better) → CF score
        // (higher = better) → Size (larger = better, sane manga fallback). Indexer priority
        // and Age are intentionally OMITTED here — they apply to release-pick selection at
        // search time (where MangaDownloadDecisionComparer already runs); by the time files
        // hit the import pipeline, both candidate CBZs are already on disk and the
        // observable "better candidate wins dedup" decision is fully covered by language +
        // CF + Size. Compare returns a value such that ThenByDescending puts BEST first.
        private sealed class LocalChapterUpgradeRankComparer : IComparer<MangaImportDecision>
        {
            private readonly ITranslationProfileService _translationProfileService;
            private readonly IConfigService _configService;

            public LocalChapterUpgradeRankComparer(
                ITranslationProfileService translationProfileService,
                IConfigService configService)
            {
                _translationProfileService = translationProfileService;
                _configService = configService;
            }

            public int Compare(MangaImportDecision x, MangaImportDecision y)
            {
                // Lower language rank = better → reversed so that ThenByDescending puts it first.
                var langCmp = -GetLanguageRank(x?.LocalChapter).CompareTo(GetLanguageRank(y?.LocalChapter));
                if (langCmp != 0)
                {
                    return langCmp;
                }

                // Higher CF score = better → natural compare so ThenByDescending puts it first.
                var cfCmp = (x?.LocalChapter?.CustomFormatScore ?? 0).CompareTo(y?.LocalChapter?.CustomFormatScore ?? 0);
                if (cfCmp != 0)
                {
                    return cfCmp;
                }

                // Larger size = better → natural compare so ThenByDescending puts it first.
                return (x?.LocalChapter?.Size ?? 0L).CompareTo(y?.LocalChapter?.Size ?? 0L);
            }

            private int GetLanguageRank(LocalChapter lc)
            {
                if (lc == null)
                {
                    return int.MaxValue;
                }

                var profileId = lc.Manga?.TranslationProfileId ?? _configService.DefaultTranslationProfileId;
                if (profileId == null)
                {
                    return int.MaxValue;
                }

                var profile = _translationProfileService.Get(profileId.Value);
                if (profile?.Languages == null)
                {
                    return int.MaxValue;
                }

                var releaseLang = lc.TranslatedLanguage ?? lc.Release?.TranslatedLanguage;
                if (string.IsNullOrEmpty(releaseLang))
                {
                    return int.MaxValue;
                }

                var rank = profile.Languages.FindIndex(l =>
                    string.Equals(l, releaseLang, StringComparison.OrdinalIgnoreCase));
                return rank < 0 ? int.MaxValue : rank;
            }
        }
    }
}
