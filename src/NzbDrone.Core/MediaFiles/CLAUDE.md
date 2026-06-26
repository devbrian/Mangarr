# NzbDrone.Core/MediaFiles

## Purpose

The **file pipeline** — disk scanning, importing completed downloads, moving/renaming on import, and lifecycle management of `ChapterFile` entities. The bridge between "downloaded data on disk" and "linked file on a monitored Chapter." Handles **CBZ/CBR/image-folder** payloads (the TV `EpisodeFile`/video machinery was deleted with `Tv/` in Phase 15).

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\MediaFiles\`

## Top-Level Files (verified at HEAD)

| File | Purpose |
|------|---------|
| `ChapterFile.cs` | The physical-file entity (`ModelBase`; renamed from `EpisodeFile.cs` in Phase 15). Carries `ScanlationGroup` (Phase 16.1 D-04 release-group axis) + `TranslatedLanguage` (BCP-47 string) + `MediaInfo` (`ChapterMediaInfo`). |
| `IChapterFileService.cs` / `ChapterFileService.cs` / `IChapterFileRepository.cs` / `ChapterFileRepository.cs` | High-level CRUD + Dapper repo. `ChapterFileService` is `IExecute<DeleteMangaFilesCommand>` (Plan 11-06). |
| `MangaDiskScanService.cs` / `IMangaDiskScanService.cs` | Library scan (`IExecute<RescanMangaCommand>`) — discover new/orphaned CBZs. |
| `RenameChapterFileService.cs` / `RenameChapterFilePreview.cs` / `RenamedChapterFile.cs` | Rename existing files based on naming config |
| `ChapterFileMovingService.cs` / `ChapterFileMoveResult.cs` | Move/copy on import |
| `MangaFileTableCleanupService.cs` / `IMangaFileTableCleanupService.cs` | Remove DB rows for missing files |
| `MangaFileExtensions.cs` / `MediaFileExtensions.cs` / `FileExtensions.cs` | Manga archive extension lists (cbz/cbr/zip/cb7) + shared helpers |
| `RecycleBinProvider.cs` / `RecycleBinException.cs` | Recycle-bin abstraction (media-agnostic, reused as-is) |
| `UpgradeChapterFileService.cs` / `IUpgradeChapterFiles.cs` | Recycle the previous CBZ on a successful upgrade (Plan 09-07; Pitfall-4 ordering lock) |
| `UpdateChapterFileService.cs` | Update file metadata |
| `MediaFileAttributeService.cs` | Set file attributes/permissions on import |
| `ImportChapterScriptService.cs` / `ScriptImport*.cs` | Custom-script import path |
| `DeletedChapterFile.cs` / `DeleteMediaFileReason.cs` / `FileDateType.cs` / `RootFolderNotFoundException.cs` / `SameFilenameException.cs` | Supporting POCOs/enums/exceptions |
| `IDeleteMediaFiles.cs` | Safe-delete contract |

## Subdirectories

### `MangaImport/` — the import pipeline
The manga import pipeline (`MangaImportDecisionMaker` + auto-discovered specs + `ImportApprovedChapters` + `Manual/` flow). See [MangaImport/CLAUDE.md](./MangaImport/CLAUDE.md).

### `ChapterArchiving/`
ComicInfo-metadata injection surface for the manga import pipeline (the in-process archiver strategy set was retired in Phase 39; the gateway delivers finished CBZs). See [ChapterArchiving/CLAUDE.md](./ChapterArchiving/CLAUDE.md).

### `MediaInfo/`
Probes a CBZ for page-image metadata (the TV `VideoFileInfoReader` is reference-preserved heritage, not the manga path).

| File | Purpose |
|------|---------|
| `ChapterMediaInfo.cs` | **Phase 30 Plan 30-05 (II2-03)** — manga peer of `MediaInfoModel`. POCO with nullable `PageCount` / `Color` / `DpiHorizontal` + non-nullable `SchemaRevision`. Round-tripped via `EmbeddedDocumentConverter<ChapterMediaInfo>` Dapper TypeHandler. JSON-serialized into the `ChapterFiles.MediaInfo` TEXT column added by Migration 004. |
| `IUpdateChapterInfo.cs` / `UpdateChapterInfoService.cs` | **Phase 30 Plan 30-05 (II2-03)** — manga peer of `IUpdateMediaInfo` / `UpdateMediaInfoService`. SixLabors.ImageSharp 3.1.12 probe per CONTEXT.md D-06. **D-05: probe-on-import only — explicitly NO `IHandle<...>` backfill daemon.** Sampling = first + middle + last page (D-07). Color detection via 100-pixel fixed-seed RNG (RESEARCH §5.3). DPI extraction accepts PixelsPerInch / PixelsPerMeter / PixelsPerCentimeter; filters the ImageSharp default 96 fallback per R-4. D-09 non-fatal: outer `try/catch` logs `Warn` + returns null on any probe failure. |

#### Pitfall 4 — step 3.5 inline probe invocation (Phase 30 Plan 30-05 Task 4)

`ImportApprovedChapters.cs` calls `_updateChapterInfoService.Update(chapterFile, lc.Manga)` at **step 3.5** of the per-decision success path — **AFTER** step 3 `_chapterFileService.Add(chapterFile)` (DB commit) and step 2 `_diskProvider.MoveFile` (filesystem move), but **BEFORE** the `ChapterImportedEvent` publish. Notification fan-out (Komga / Kavita rescan handlers) fires on `ChapterImportedEvent`; if it published before `MediaInfo` populated, the rescan would see a fresh-but-incomplete row. The invocation is wrapped in its own `try/catch` so any probe failure leaves the import success path intact (D-09 non-fatal).

### `Commands/` / `Events/`
Manga-shape rescan/rename/delete commands + `ChapterFile`/import lifecycle events (the TV `EpisodeFile*`/`RescanSeries`/`RetagSeries` peers were deleted with `Tv/`). `TorrentInfo/` is reference-preserved heritage.

## Phase 6 Manga Siblings

Phase 6 ships the manga import pipeline + supporting events as parallel siblings to the TV pipeline. Subsequent phases (Phase 9 service-sweep backfill, Phase 11 commands-and-execute sweep) extend the manga sibling surface as audits surface gaps. **Phase 14 cleanup** will collapse the sibling pairs when `Tv/` deletes.

| Sibling | Phase / Plan | Notes |
|---------|--------------|-------|
| [`MediaFiles/MangaImport/`](./MangaImport/CLAUDE.md) | Plan 06-07 | Manga import pipeline. `MangaImportDecisionMaker` (auto-discovery via `IEnumerable<IMangaImportDecisionEngineSpecification>`) + 6 specs (`ChapterFileExists`, `NotEmptyArchive`, `MatchesGrab`, `FreeSpace`, `Upgrade` with D-10 three-state, `AlreadyImported` with BL-01 fix) + `ImportApprovedChapters` orchestrator. **PITFALL 4 ORDERING INVARIANT**: `ChapterImportedEvent` published as the LAST line of the success path — Komga/Kavita rescan handlers race-fire on this event so DB commit + filesystem move must complete first. POCOs: `LocalChapter`, `MangaImportSpecDecision`, `MangaImportResult`, `MangaImportDecision`. Phase 8 cleanup: collapse with `EpisodeImport/`. |
| `MediaFiles/MangaImport/ChapterImportedEvent.cs` | Plan 06-02 | Manga sibling of `MediaFiles/Events/EpisodeImportedEvent.cs`. Carries `Manga + Chapter + ChapterFile + DownloadClientItem + NewDownload + SourcePath`. Pitfall 4 GUARD: published AFTER ChapterFile DB commit + filesystem move complete (Plan 06-07 `ImportApprovedChapters` enforces). Phase 8 cleanup: collapse with `EpisodeImportedEvent`. |
| `MediaFiles/MangaImport/ChapterImportFailedEvent.cs` | Plan 06-02 | Carries `Manga + Chapter + SourcePath + FailureReason + DownloadClientItem`. Covers IMPORT-stage failures (post-archive, pre-DB-commit) — distinct from Phase 4 `ChapterDownloadFailedEvent` (download-stage). Phase 8 cleanup: collapse with hypothetical `EpisodeImportFailedEvent` if/when `Tv/` deletes. |
| `MediaFiles/ChapterArchiving/ChapterGrabbedEvent.cs` | Plan 06-01 | Carries `RemoteChapter + DownloadId + DownloadClient`. Emitted by `MangaDownloadService` on grab (the Phase-4 `InProcessImageDownloadClient` that originally emitted it was retired in Phase 39); consumed by Plan 06-03 `ChapterHistoryService.Handle` + the Phase-36 monitoring loop. |
| `MediaFiles/ChapterArchiving/ChapterDownloadFailedEvent.cs` un-sealed + extended | Plan 06-01 | Un-sealed; gained optional `SourceTitle / Source / DownloadClient / Release : ReleaseInfo` init-only properties + `Reason / Message` aliases. Existing 4-arg ctor preserved. Plans 06-03/04/08 require. |
| `ChapterFile.cs` + `ChapterFileService` + `ChapterFileRepository` + `ChapterFileAddedEvent` + `ChapterFileDeletedEvent` | Plan 06-01 | Parallel sibling to `EpisodeFile` + `MediaFileService` + `MediaFileRepository` + `EpisodeFileAddedEvent` + `EpisodeFileDeletedEvent`. `ChapterFileService` `IHandleAsync<MangaDeletedEvent>` cascade-deletes mirror `MediaFileService` `IHandleAsync<SeriesDeletedEvent>`. Phase 8 cleanup: collapse with `EpisodeFile`. |
| `ChapterFileService.cs:Execute(DeleteMangaFilesCommand)` — `IExecute<DeleteMangaFilesCommand>` handler | [Plan 11-06](../../../.planning/phases/11-commands-and-execute-sweep/11-06-SUMMARY.md) | Manga-side analog of `MediaFileDeletionService.IExecute<DeleteSeriesFilesCommand>` (TV peer body lines 102-175). Closes Risk 1 (silent `UnknownCommandExecutor` fallback for manga bulk-delete-files commands — class shipped Phase 8 cluster-02 Plan 02-01 without handler; gap caught by Phase 11 Axis-1 sweep). 5 NEW ctor deps (`IMangaService`, `IRootFolderService`, `IDiskProvider`, `IRecycleBinProvider`, `ICommandResultReporter`). **Pitfall 4 ordering** — recycle FIRST, DB row delete SECOND (the inverse leaks files on disk if `_recycleBinProvider.DeleteFile` throws); `MockSequence` test in `ChapterFileServiceDeleteMangaFilesFixture` (6 NUnit tests) enforces. Per-mangaId try/catch isolation; `CommandResult.Indeterminate` reported on each guard-clause continue. DryIoc auto-discovers via `Bootstrap.AutoAddServices` (no manual `Container.Register`). Phase 14 cleanup: collapse with `MediaFileDeletionService.Execute(DeleteSeriesFilesCommand)`. |
| `UpgradeChapterFileService.cs` + `IUpgradeChapterFiles` | Plan 09-07 (D-09-05) | Parallel sibling to `UpgradeMediaFileService` + `IUpgradeMediaFiles`. Backfills the missing side effect of the D-10 three-state upgrade-allowed gate (Phase 6 `ImportApprovedChapters` enforced the decision but did NOT recycle the previous CBZ — every successful upgrade silently leaked the old file on disk). Reuses media-agnostic `IRecycleBinProvider` (no manga sibling needed). **PITFALL 4 ORDERING LOCK** (RESEARCH §Threat 535 / PATTERNS §A): `_recycleBinProvider.DeleteFile` MUST run BEFORE `_chapterFileService.Delete` — mirrors TV `UpgradeMediaFileService.cs:65` then `:73` verbatim. Inverse leaks the file path on disk if the recycle step throws. Recycle-only mode (first-arg `null`) is used by `ImportApprovedChapters` step 0.5 — caller already owns the new-file move via `IDiskProvider.MoveFile`. Phase 14 cleanup: collapse with `UpgradeMediaFileService`. |
| `MangaDiskScanService.cs` + `IMangaDiskScanService` + `MangaFileTableCleanupService.cs` + `IMangaFileTableCleanupService` + `MangaFileExtensions.cs` | Plan 09-06 (D-09-03 #1) | Parallel sibling pair to `DiskScanService` + `MediaFileTableCleanupService` + `MediaFileExtensions`. Ships as ONE atomic plan per D-09-03 #1 because TV `DiskScanService.cs:193` directly calls `_mediaFileTableCleanupService.Clean(...)` — half-broken DiskScan would leave dangling rows. `MangaDiskScanService` is `IExecute<RescanMangaCommand>` + walks `Manga.Path` filtered by `MangaFileExtensions.Extensions` (cbz/cbr/zip/cb7) → calls `_chapterFileTableCleanupService.Clean(...)` → builds `LocalChapter` aggregates via `IMangaParsingService.Map` (manga divergence — TV's `IMakeImportDecision` parses internally; manga `IMakeMangaImportDecision.GetImportDecisions(List<LocalChapter>, DownloadClientItem)` requires caller-built objects, mirrors `ManualImportService.ProcessFolder`) → `_importApprovedChapters.Import(...)` → publishes `MangaScannedEvent` (Pitfall 4 LAST). **OMITS `IUpdateMediaInfo`** per D-09-04 (UpdateChapterInfoService deferred to v1.1 — `MangaFileNameBuilder` runs without `{Page Count}`/`{Color}`/`{DPI}` tokens). **OMITS `MangaScanSkippedEvent`** (no v1 sibling — log + return). `MangaFileTableCleanupService.Clean` mirrors TV verbatim: cascade-delete orphan `ChapterFile` rows + clear `Chapter.ChapterFileId` (nullable int? — uses `GetValueOrDefault()` guards). Phase 14 cleanup: collapse with `DiskScanService` + `MediaFileTableCleanupService`. |

## Cross-References

- [../CLAUDE.md](../CLAUDE.md) — NzbDrone.Core overview
- [../Manga/CLAUDE.md](../Manga/CLAUDE.md) — Chapter that ChapterFile is linked to (the Sonarr `Tv/` Episode analog was deleted in Phase 15)
- [../Download/CLAUDE.md](../Download/CLAUDE.md) — Source of completed downloads
- [../Organizer/](../Organizer/) — `FileNameBuilder` token system
- [../Parser/CLAUDE.md](../Parser/CLAUDE.md) — Parses filenames during scan/import
- [../DecisionEngine/CLAUDE.md](../DecisionEngine/CLAUDE.md) — Same Specification pattern
