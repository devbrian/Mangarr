# NzbDrone.Core/MediaFiles

## Purpose

The **file pipeline** — disk scanning, importing completed downloads, moving/renaming on import, and lifecycle management of `EpisodeFile` entities. The bridge between "downloaded data on disk" and "linked file on a monitored Episode."

For Mangarr: this is where **CBZ/CBR/image-folder** handling will diverge most from Sonarr's video-file handling.

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

| Sonarr | Mangarr | Notes |
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

## Cross-References

- [../CLAUDE.md](../CLAUDE.md) — NzbDrone.Core overview
- [../Tv/CLAUDE.md](../Tv/CLAUDE.md) — Episode that EpisodeFile is linked to
- [../Download/CLAUDE.md](../Download/CLAUDE.md) — Source of completed downloads
- [../Organizer/](../Organizer/) — `FileNameBuilder` token system
- [../Parser/CLAUDE.md](../Parser/CLAUDE.md) — Parses filenames during scan/import
- [../DecisionEngine/CLAUDE.md](../DecisionEngine/CLAUDE.md) — Same Specification pattern
