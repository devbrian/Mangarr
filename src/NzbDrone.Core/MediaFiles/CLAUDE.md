# NzbDrone.Core/MediaFiles

## Purpose

The **file pipeline** — disk scanning, importing completed downloads, moving/renaming on import, and lifecycle management of `EpisodeFile` entities. The bridge between "downloaded data on disk" and "linked file on a monitored Episode."

For Mangarr: this is where **CBZ/CBR/image-folder** handling will diverge most from Mangarr's video-file handling.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\MediaFiles\`

## Top-Level Files

| File | Purpose |
|------|---------|
| `EpisodeFile.cs` | The physical-file entity (`ModelBase`). 14 properties: SeriesId, SeasonNumber, Path, RelativePath, Size, DateAdded, SceneName, ReleaseGroup, ReleaseHash, OriginalFilePath, Quality, Languages, IndexerFlags, ReleaseType. Plus `LazyLoaded<List<Episode>>` and `LazyLoaded<Series>`. |
| `MediaFile.cs` (if present) | Abstract base |
| `IMediaFileService.cs` / `MediaFileService.cs` | High-level CRUD |
| `EpisodeFileRepository.cs` | Dapper repo |
| `DiskScanService.cs` | Library scan — discover new/orphaned files |
| `RenameEpisodeFileService.cs` | Rename existing files based on naming config |
| `EpisodeFileMovingService.cs` | Move files into series folder layout (used at import) |
| `MediaFileDeletionService.cs` | Safe-delete (move to recycle bin if configured) |
| `MoveEpisodeFiles.cs` | Bulk-move command |
| `MediaFileTableCleanupService.cs` | Remove DB rows for missing files |
| `MediaFileExtensions.cs` | File extension allow/deny lists for video |
| `RecycleBinProvider.cs` | Recycle bin abstraction |
| `UpgradeMediaFileService.cs` | Replace existing file with upgrade |
| `OtherExtras/` | Subtitle, NFO files |
| `IDownloadedEpisodesImportService.cs` / `DownloadedEpisodesImportService.cs` | Top-level import entrypoint |

## Subdirectories

### `EpisodeImport/` — The Import Pipeline
The most complex sub-area. Imports completed downloads into the library.

| File / Subdir | Purpose |
|---------------|---------|
| `IMakeImportDecision.cs` / `ImportDecisionMaker.cs` | Top-level orchestrator, runs Specifications |
| `ImportDecision.cs` | Per-file approve/reject result |
| `ImportApprovedEpisodes.cs` | Move/copy approved imports into place, create EpisodeFile rows |
| `ImportMode.cs` | enum `Auto` / `Move` / `Copy` |
| `Aggregators/` | Aggregate metadata from multiple sources (file path, parsed title, mediainfo) |
| `Specifications/` | Import specs (separate hierarchy from DecisionEngine specs) |
| `Manual/` | Manual import (user-initiated) flow |

### Import Specifications (`EpisodeImport/Specifications/`)
| Spec | Purpose |
|------|---------|
| `NotSampleSpecification` | Reject files that look like samples |
| `MatchesFolderSpecification` | File parsed correctly relative to its folder |
| `UpgradeSpecification` | Is the new file an upgrade over existing? |
| `ImportNotForDownloadClientItemSpecification` | Match item to a tracked download |
| `MultiEpisodeFileSpecification` | Don't import a multi-ep file when single is better |
| `FreeSpaceSpecification` | Disk has space for the move |
| `NotInUseSpecification` | File is not locked by another process |
| `NotUnpackingSpecification` | File is not actively being extracted |
| `AlreadyImportedSpecification` | Don't re-import |
| `GrabbedReleaseQualitySpecification` | New file matches grabbed quality |

### `MediaInfo/`
| File | Purpose |
|------|---------|
| `IMediaInfoService.cs` / `VideoFileInfoReader.cs` | Probe video for codec/resolution/bitrate/runtime |
| `MediaInfoModel.cs` | DTO of probed metadata |
| `MediaInfoFormatter.cs` | Format mediainfo for filename tokens |
| `UpdateMediaInfoService.cs` | Refresh mediainfo for existing files |

### `TorrentInfo/`
| File | Purpose |
|------|---------|
| `TorrentFileInfoReader.cs` | Parse `.torrent` file (file list, info hash) |
| `BEncode*` | bencode parser |

### `Commands/`
- `RescanSeriesCommand` — refresh disk scan
- `RenameFilesCommand` — apply rename
- `RetagSeriesCommand` — re-write tag metadata
- `RescanFolders`, `RescanRootFolder`, etc.

### `Events/`
| Event | Triggered By |
|-------|--------------|
| `EpisodeFileAddedEvent` | New file linked to episode |
| `EpisodeFileDeletedEvent` | File removed |
| `EpisodeImportedEvent` | Successful import |
| `EpisodeFolderCreatedEvent` | New series folder created |
| `MediaCoversUpdatedEvent` | Posters/banners refreshed |

## Disk Scan Flow

```
RescanSeriesCommand for series X
        ↓
DiskScanService.Scan(series)
        ├─ Get all video files in series.Path
        ├─ For each file: try to parse + map to Episode
        ├─ Compare to existing EpisodeFile rows
        ├─ Create new EpisodeFile rows for new files
        ├─ Mark missing files (DB row, no disk file) as deleted
        └─ Run MediaFileTableCleanupService
```

## Import Flow (Completed Download)

```
CompletedDownloadService detects completion
        ↓
DownloadedEpisodesImportService.ProcessRootFolder(...)
        ↓
ImportDecisionMaker.GetImportDecisions(files, series)
        ├─ For each file: parse, aggregate metadata
        └─ Run Specifications → ImportDecision
        ↓
ImportApprovedEpisodes.Import(decisions)
        ├─ For each approved decision:
        │   ├─ EpisodeFileMovingService.MoveEpisodeFile (or copy)
        │   ├─ Create / update EpisodeFile row
        │   ├─ Link Episode.EpisodeFileId
        │   └─ Optionally delete previous (UpgradeMediaFileService)
        ↓
EpisodeFileAddedEvent + EpisodeImportedEvent
        ↓
Notifications + SignalR
```

## Naming & Path Building

Filenames built from `Organizer/FileNameBuilder.cs` based on `NamingConfig`. Tokens like:
- `{Series Title}` → "My Show"
- `{season:00}` → "01"
- `{episode:00}` → "02"
- `{Quality Title}` → "1080p"
- `{Release Group}` → "GROUP"
- `{Original Title}` → original release name

Series folder path comes from `Tv/SeriesPathBuilder.cs`.

## Manga Adaptation Plan

### Concepts to Adapt

| Mangarr | Mangarr | Notes |
|--------|---------|-------|
| `EpisodeFile` | `ChapterFile` | Different metadata: PageCount, ScanlationGroup, FileFormat (CBZ/CBR/Folder/PDF) |
| `MediaInfo` (codec, resolution, runtime) | `ChapterInfo` (page count, image format, average DPI) | Different probe |
| Video file extensions (mkv, mp4, avi…) | Manga formats (cbz, cbr, cb7, zip, rar, pdf, folder of jpg/png/webp) | New `MediaFileExtensions` list |
| Sample file detection | Sample/preview detection (single-page placeholders, "0001 - sample") | New rule |
| Multi-episode file | Multi-chapter file (e.g., `c001-005.cbz`) | New parsing |
| `DiskScanService` | Mostly reusable | Update file-extension list |
| `EpisodeFileMovingService` | Mostly reusable | Just operates on whatever path |
| `FileNameBuilder` | Update token list | New tokens: `{Chapter Number}`, `{Volume}`, `{Scanlation Group}`, `{Manga Title}` |

### MediaInfo Replacement
Video probing (codec, audio tracks) is irrelevant for manga. Replace `VideoFileInfoReader` with a `MangaFileInfoReader` that:
- Counts pages in the CBZ/CBR/folder
- Detects image format(s) (jpg/png/webp)
- Optionally measures average DPI
- Detects color vs B&W

### New File Types
Add a `MangaFileFormat` enum and field on `ChapterFile`:
- `Cbz`, `Cbr`, `Cb7`, `Zip`, `Rar`, `Folder`, `Pdf`, `Epub`, `Mobi`

### Import Pipeline
The pipeline architecture transfers cleanly. Replace specs:
- `NotSampleSpecification` → adapt to manga (or keep as-is if you want)
- Keep `FreeSpaceSpecification`, `NotInUseSpecification`, `AlreadyImportedSpecification`, `UpgradeSpecification`
- Add `MinimumPageCountSpecification` (reject extras / sample chapters with too few pages)

## Phase 6 Manga Siblings

Phase 6 ships the manga import pipeline + supporting events as parallel siblings to the TV pipeline. Subsequent phases (Phase 9 service-sweep backfill, Phase 11 commands-and-execute sweep) extend the manga sibling surface as audits surface gaps. **Phase 14 cleanup** will collapse the sibling pairs when `Tv/` deletes.

| Sibling | Phase / Plan | Notes |
|---------|--------------|-------|
| [`MediaFiles/MangaImport/`](./MangaImport/CLAUDE.md) | Plan 06-07 | Manga import pipeline. `MangaImportDecisionMaker` (auto-discovery via `IEnumerable<IMangaImportDecisionEngineSpecification>`) + 6 specs (`ChapterFileExists`, `NotEmptyArchive`, `MatchesGrab`, `FreeSpace`, `Upgrade` with D-10 three-state, `AlreadyImported` with BL-01 fix) + `ImportApprovedChapters` orchestrator. **PITFALL 4 ORDERING INVARIANT**: `ChapterImportedEvent` published as the LAST line of the success path — Komga/Kavita rescan handlers race-fire on this event so DB commit + filesystem move must complete first. POCOs: `LocalChapter`, `MangaImportSpecDecision`, `MangaImportResult`, `MangaImportDecision`. Phase 8 cleanup: collapse with `EpisodeImport/`. |
| `MediaFiles/MangaImport/ChapterImportedEvent.cs` | Plan 06-02 | Manga sibling of `MediaFiles/Events/EpisodeImportedEvent.cs`. Carries `Manga + Chapter + ChapterFile + DownloadClientItem + NewDownload + SourcePath`. Pitfall 4 GUARD: published AFTER ChapterFile DB commit + filesystem move complete (Plan 06-07 `ImportApprovedChapters` enforces). Phase 8 cleanup: collapse with `EpisodeImportedEvent`. |
| `MediaFiles/MangaImport/ChapterImportFailedEvent.cs` | Plan 06-02 | Carries `Manga + Chapter + SourcePath + FailureReason + DownloadClientItem`. Covers IMPORT-stage failures (post-archive, pre-DB-commit) — distinct from Phase 4 `ChapterDownloadFailedEvent` (download-stage). Phase 8 cleanup: collapse with hypothetical `EpisodeImportFailedEvent` if/when `Tv/` deletes. |
| `MediaFiles/ChapterArchiving/ChapterGrabbedEvent.cs` | Plan 06-01 | Carries `RemoteChapter + DownloadId + DownloadClient`. Role-match analog: `Download/EpisodeGrabbedEvent.cs`. Emitted by Phase 4 `InProcessImageDownloadClient` after grab; consumed by Plan 06-03 `ChapterHistoryService.Handle`. Phase 8 cleanup: collapse with `EpisodeGrabbedEvent`. |
| `MediaFiles/ChapterArchiving/ChapterDownloadFailedEvent.cs` un-sealed + extended | Plan 06-01 | Un-sealed; gained optional `SourceTitle / Source / DownloadClient / Release : ReleaseInfo` init-only properties + `Reason / Message` aliases. Existing 4-arg ctor preserved. Plans 06-03/04/08 require. |
| `ChapterFile.cs` + `ChapterFileService` + `ChapterFileRepository` + `ChapterFileAddedEvent` + `ChapterFileDeletedEvent` | Plan 06-01 | Parallel sibling to `EpisodeFile` + `MediaFileService` + `MediaFileRepository` + `EpisodeFileAddedEvent` + `EpisodeFileDeletedEvent`. `ChapterFileService` `IHandleAsync<MangaDeletedEvent>` cascade-deletes mirror `MediaFileService` `IHandleAsync<SeriesDeletedEvent>`. Phase 8 cleanup: collapse with `EpisodeFile`. |
| `ChapterFileService.cs:Execute(DeleteMangaFilesCommand)` — `IExecute<DeleteMangaFilesCommand>` handler | [Plan 11-06](../../../.planning/phases/11-commands-and-execute-sweep/11-06-SUMMARY.md) | Manga-side analog of `MediaFileDeletionService.IExecute<DeleteSeriesFilesCommand>` (TV peer body lines 102-175). Closes Risk 1 (silent `UnknownCommandExecutor` fallback for manga bulk-delete-files commands — class shipped Phase 8 cluster-02 Plan 02-01 without handler; gap caught by Phase 11 Axis-1 sweep). 5 NEW ctor deps (`IMangaService`, `IRootFolderService`, `IDiskProvider`, `IRecycleBinProvider`, `ICommandResultReporter`). **Pitfall 4 ordering** — recycle FIRST, DB row delete SECOND (the inverse leaks files on disk if `_recycleBinProvider.DeleteFile` throws); `MockSequence` test in `ChapterFileServiceDeleteMangaFilesFixture` (6 NUnit tests) enforces. Per-mangaId try/catch isolation; `CommandResult.Indeterminate` reported on each guard-clause continue. DryIoc auto-discovers via `Bootstrap.AutoAddServices` (no manual `Container.Register`). Phase 14 cleanup: collapse with `MediaFileDeletionService.Execute(DeleteSeriesFilesCommand)`. |
| `UpgradeChapterFileService.cs` + `IUpgradeChapterFiles` | Plan 09-07 (D-09-05) | Parallel sibling to `UpgradeMediaFileService` + `IUpgradeMediaFiles`. Backfills the missing side effect of the D-10 three-state upgrade-allowed gate (Phase 6 `ImportApprovedChapters` enforced the decision but did NOT recycle the previous CBZ — every successful upgrade silently leaked the old file on disk). Reuses media-agnostic `IRecycleBinProvider` (no manga sibling needed). **PITFALL 4 ORDERING LOCK** (RESEARCH §Threat 535 / PATTERNS §A): `_recycleBinProvider.DeleteFile` MUST run BEFORE `_chapterFileService.Delete` — mirrors TV `UpgradeMediaFileService.cs:65` then `:73` verbatim. Inverse leaks the file path on disk if the recycle step throws. Recycle-only mode (first-arg `null`) is used by `ImportApprovedChapters` step 0.5 — caller already owns the new-file move via `IDiskProvider.MoveFile`. Phase 14 cleanup: collapse with `UpgradeMediaFileService`. |
| `MangaDiskScanService.cs` + `IMangaDiskScanService` + `MangaFileTableCleanupService.cs` + `IMangaFileTableCleanupService` + `MangaFileExtensions.cs` | Plan 09-06 (D-09-03 #1) | Parallel sibling pair to `DiskScanService` + `MediaFileTableCleanupService` + `MediaFileExtensions`. Ships as ONE atomic plan per D-09-03 #1 because TV `DiskScanService.cs:193` directly calls `_mediaFileTableCleanupService.Clean(...)` — half-broken DiskScan would leave dangling rows. `MangaDiskScanService` is `IExecute<RescanMangaCommand>` + walks `Manga.Path` filtered by `MangaFileExtensions.Extensions` (cbz/cbr/zip/cb7) → calls `_chapterFileTableCleanupService.Clean(...)` → builds `LocalChapter` aggregates via `IMangaParsingService.Map` (manga divergence — TV's `IMakeImportDecision` parses internally; manga `IMakeMangaImportDecision.GetImportDecisions(List<LocalChapter>, DownloadClientItem)` requires caller-built objects, mirrors `ManualImportService.ProcessFolder`) → `_importApprovedChapters.Import(...)` → publishes `MangaScannedEvent` (Pitfall 4 LAST). **OMITS `IUpdateMediaInfo`** per D-09-04 (UpdateChapterInfoService deferred to v1.1 — `MangaFileNameBuilder` runs without `{Page Count}`/`{Color}`/`{DPI}` tokens). **OMITS `MangaScanSkippedEvent`** (no v1 sibling — log + return). `MangaFileTableCleanupService.Clean` mirrors TV verbatim: cascade-delete orphan `ChapterFile` rows + clear `Chapter.ChapterFileId` (nullable int? — uses `GetValueOrDefault()` guards). Phase 14 cleanup: collapse with `DiskScanService` + `MediaFileTableCleanupService`. |

## Cross-References

- [../CLAUDE.md](../CLAUDE.md) — NzbDrone.Core overview
- [../Tv/CLAUDE.md](../Tv/CLAUDE.md) — Episode that EpisodeFile is linked to
- [../Download/CLAUDE.md](../Download/CLAUDE.md) — Source of completed downloads
- [../Organizer/](../Organizer/) — `FileNameBuilder` token system
- [../Parser/CLAUDE.md](../Parser/CLAUDE.md) — Parses filenames during scan/import
- [../DecisionEngine/CLAUDE.md](../DecisionEngine/CLAUDE.md) — Same Specification pattern
