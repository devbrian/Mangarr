using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MediaFiles.ChapterArchiving.Metadata.ComicInfo;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.MediaFiles.MangaImport.Manual;

// Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — MediaFiles/EpisodeImport/ DELETED.
//   using NzbDrone.Core.MediaFiles.EpisodeImport; ← deleted
using NzbDrone.Core.MediaFiles.MediaInfo;
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
    //       5. _eventAggregator.PublishEvent(new ChapterImportedEvent { ... })  ← LAST LINE
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
    //   * Phase 39 RETIRE-01: the former step-5 Phase-4 ChapterDownloadState row delete
    //     (lifecycle hook) was REMOVED with the in-process download vertical — the gateway
    //     path no longer creates ChapterDownloadState rows, so there is nothing to delete.
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
        private readonly IEventAggregator _eventAggregator;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly ITranslationProfileService _translationProfileService;
        private readonly IConfigService _configService;
        private readonly IUpgradeChapterFiles _upgradeChapterFileService;   // Phase 9 D-09-05
        private readonly IUpdateChapterInfo _updateChapterInfoService;      // Phase 30 Plan 30-05 (II2-03)
        private readonly IComicInfoCbzInjector _comicInfoCbzInjector;       // Phase 38 Plan 38-02 (CINFO-01)
        private readonly Logger _logger;

        public ImportApprovedChapters(
            IChapterFileService chapterFileService,
            IChapterService chapterService,
            IDiskProvider diskProvider,
            IBuildMangaPaths pathBuilder,
            IEventAggregator eventAggregator,
            IManageCommandQueue commandQueueManager,
            ITranslationProfileService translationProfileService,
            IConfigService configService,
            IUpgradeChapterFiles upgradeChapterFileService,                  // Phase 9 D-09-05
            IUpdateChapterInfo updateChapterInfoService,                      // Phase 30 Plan 30-05 (II2-03)
            IComicInfoCbzInjector comicInfoCbzInjector,                       // Phase 38 Plan 38-02 (CINFO-01)
            Logger logger)
        {
            _chapterFileService = chapterFileService;
            _chapterService = chapterService;
            _diskProvider = diskProvider;
            _pathBuilder = pathBuilder;
            _eventAggregator = eventAggregator;
            _commandQueueManager = commandQueueManager;
            _translationProfileService = translationProfileService;
            _configService = configService;
            _upgradeChapterFileService = upgradeChapterFileService;
            _updateChapterInfoService = updateChapterInfoService;
            _comicInfoCbzInjector = comicInfoCbzInjector;
            _logger = logger;
        }

        public List<MangaImportResult> Import(
            List<MangaImportDecision> decisions,
            bool newDownload,
            DownloadClientItem downloadClientItem = null,
            bool overwriteExisting = false)
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
                    // publish at step 5). The presence of a prior ChapterFileId means the decision passed
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

                        // Phase 25 Plan 25-04 Task 7 (D-04 + v2-02 promotion) —
                        // per-decision lc.ExistingFileBehavior wins over the
                        // batch-level overwriteExisting fallback (RESEARCH Open
                        // Q #1 recommendation). Either input forces overwrite;
                        // both default to Skip preserves no-destructive-default
                        // safety.
                        var perRowOverwrite =
                            lc.ExistingFileBehavior == ExistingFileBehavior.Replace
                            || overwriteExisting;
                        _diskProvider.MoveFile(lc.Path, destinationPath, perRowOverwrite);
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
                        ScanlationGroup = lc.ScanlationGroup ?? lc.Release?.ScanlationGroup ?? lc.Release?.Indexer
                    };

                    chapterFile = _chapterFileService.Add(chapterFile);

                    // ---- 3.5 Plan 30-05 (II2-03 D-05) — ImageSharp probe inline (probe-on-import only; NO daemon).
                    // PITFALL 4 ORDERING PRESERVED: runs AFTER step 3 DB write + filesystem move (step 2)
                    // + BEFORE step 5 ChapterImportedEvent publish. Probe populates chapterFile.MediaInfo
                    // (PageCount + Color + DpiHorizontal) via ImageSharp 3.1.12 sampling first + middle + last
                    // page (D-07). D-09 non-fatal: probe failures log Warn + leave MediaInfo null; import
                    // succeeds. Token render (MangaFileNameBuilder) skips null per D-09 (no "0 pages" defaults).
                    try
                    {
                        _updateChapterInfoService.Update(chapterFile, lc.Manga);
                    }
                    catch (Exception probeEx)
                    {
                        _logger.Warn(probeEx, "ImageSharp probe failed for {0}; MediaInfo left null", chapterFile.Path);
                    }

                    // ---- 3.5b Phase 38 Plan 38-02 (CINFO-01) — ComicInfo.xml injection ----
                    // PITFALL 4 ORDERING PRESERVED: runs AFTER step 3 DB write + filesystem move (step 2)
                    // + BEFORE step 5 ChapterImportedEvent publish (the gateway delivers page-only CBZs,
                    // so this injector is the SOLE ComicInfo writer; Komga/Kavita rescan handlers fire on
                    // ChapterImportedEvent and MUST see a metadata-complete archive).
                    //
                    // D-A — invoked UNCONDITIONALLY (no gateway-only gate, no download-client-type
                    // discriminator, no ComicInfo-presence check). Placed after the ImageSharp probe so a
                    // probe-populated MediaInfo is committed first (D-B2 — ordering vs probe is not a
                    // correctness constraint).
                    //
                    // D-B1 — DELIBERATELY NOT wrapped in its own swallow-everything try/catch (UNLIKE the
                    // non-fatal ImageSharp probe above). The injector's OWN Warn->retry-once->fatal ladder
                    // handles the transient-retry internally; a persisted failure RE-THROWS and propagates
                    // to the enclosing per-decision catch (Exception ex) below, which publishes
                    // ChapterImportFailedEvent ONCE (no duplicate publish). The chapter then does NOT land
                    // and the Phase-36 auto-retry orchestrator handles it.
                    //
                    // GH #311 review (P2): GATED to ZIP-based containers. The shared import path also
                    // accepts .cbr (RAR) / .cb7 (7z) via MangaFileExtensions (disk-scan + manual import),
                    // and ComicInfoCbzInjector opens the archive with ZipArchiveMode.Update — which throws
                    // on a non-ZIP file. Because this runs AFTER _chapterFileService.Add, an unconditional
                    // call would fail the import of a perfectly valid RAR/7z manga archive *after* the row
                    // was committed. Non-ZIP formats import WITHOUT ComicInfo injection; the gateway always
                    // delivers .cbz (D-D), so the gateway path is unaffected.
                    var chapterFileExtension = Path.GetExtension(chapterFile.Path);
                    if (chapterFileExtension.Equals(".cbz", StringComparison.OrdinalIgnoreCase) ||
                        chapterFileExtension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            _comicInfoCbzInjector.Inject(chapterFile, lc.Manga, lc.Chapter);
                        }
                        catch (Exception injectEx)
                        {
                            // GH #311 review: the ChapterFile row was already committed in step 3, but
                            // the FK wire (step 4) + ChapterImportedEvent (step 5) have NOT run yet. A
                            // persisted injection failure re-throws to the per-decision catch below (which
                            // publishes ChapterImportFailedEvent) — so without this rollback the committed
                            // ChapterFile would be ORPHANED (no Chapter points at it) while the import is
                            // reported failed. Delete the row first, THEN re-throw; a later disk-scan
                            // re-discovers the moved CBZ and re-imports cleanly.
                            _logger.Debug(
                                injectEx,
                                "ComicInfo injection failed for {0}; rolling back persisted ChapterFile {1}",
                                chapterFile.Path,
                                chapterFile.Id);
                            _chapterFileService.Delete(chapterFile, DeleteMediaFileReason.Manual);
                            throw;
                        }
                    }
                    else
                    {
                        _logger.Debug(
                            "Skipping ComicInfo injection for non-ZIP archive {0} ({1}); injector requires .cbz/.zip",
                            chapterFile.Path,
                            chapterFileExtension);
                    }

                    // ---- 4. Wire Chapter.ChapterFileId FK (Plan 06-01 PIPELINE-04 column) ----
                    lc.Chapter.ChapterFileId = chapterFile.Id;
                    _chapterService.UpdateChapter(lc.Chapter);

                    // ---- 5. PITFALL 4 GUARD — PublishEvent is the LAST line in the success path ----
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
