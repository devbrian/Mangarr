# MediaFiles/MangaImport

## Purpose

Phase 6 PIPELINE-04 manga sibling of `src/NzbDrone.Core/MediaFiles/EpisodeImport/`. Ships the manga import pipeline: `LocalChapter` POCO → `MangaImportDecisionMaker` (auto-discovered specs) → `ImportApprovedChapters` orchestrator that produces a `ChapterFile` row + emits `ChapterImportedEvent` for downstream rescan notifications (Komga / Kavita — Plans 06-10/11).

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\MediaFiles\MangaImport`

## Key Files

| File | Purpose |
|------|---------|
| `LocalChapter.cs` | POCO carrying staging CBZ path + resolved `Manga` + `Chapter` aggregate. Mirrors `LocalEpisode` shape with manga divergences (no QualityModel; `Release : ReleaseInfo` direct, not `GrabbedReleaseInfo` wrapper). Includes nested `DownloadClientItemInfo` to keep the file free of circular usings. |
| `MangaImportSpecDecision.cs` | Per-spec accept/reject result. Cached `Accept()` singleton + `Reject(reason, message, args)` factory. `ImportRejectionReason` enum drops TV-only `Sample/SeasonExtra/PartialSeason/NotQualityUpgrade/NotRevisionUpgrade`; adds manga-shaped `ChapterFileExists/EmptyArchive/ChapterNotFoundInRelease/NotUpgradeAllowed/ChapterAlreadyImported/RootFolderMissing/DecisionError`. |
| `MangaImportResult.cs` | Per-decision outcome. `MangaImportResultType` enum: `Imported/Skipped/Rejected`. |
| `MangaImportDecision.cs` | Per-LocalChapter aggregate of all spec rejections. `Approved == !Rejections.Any()`. Wraps `(Reason, Message)` pairs in `MangaImportRejection`. |
| `IMangaImportDecisionEngineSpecification.cs` | Single-method contract for each import spec. Auto-discovered via DryIoc `IEnumerable<>` ctor injection. |
| `IImportApprovedChapters.cs` + `ImportApprovedChapters.cs` | Orchestrator service. Pitfall 4 mitigation enforced (see below). |
| `MangaImportDecisionMaker.cs` (`IMakeMangaImportDecision`) | Batch evaluator. Auto-resolves all specs via `IEnumerable<>`. Per-spec try/catch isolation. |
| `ChapterImportedEvent.cs` (Plan 06-02) | Emitted by `ImportApprovedChapters` ONLY after DB commit + file move complete. Komga/Kavita providers subscribe via `INotification.OnChapterImport`. |
| `ChapterImportFailedEvent.cs` (Plan 06-02) | Emitted on any per-decision exception. Plan 06-08 auto-retry orchestrator subscribes. |
| `Specifications/ChapterFileExistsSpecification.cs` | Idempotency guard — rejects when `Chapter.ChapterFileId > 0`. |
| `Specifications/NotEmptyArchiveSpecification.cs` | Rejects when `LocalChapter.Size == 0` (Phase 4 produced empty CBZ). |
| `Specifications/MatchesGrabSpecification.cs` | Rejects when `LocalChapter.Chapters[].Id` doesn't match `ReleaseInfo.ChapterIds` (downloaded the wrong thing). |
| `Specifications/FreeSpaceSpecification.cs` | TV-canonical body; type-swap (`Series.Path → Manga.Path`). |
| `Specifications/UpgradeSpecification.cs` | D-10 three-state effective-upgrade-allowed. See [D-10 section](#d-10-three-state-effectiveupgradeallowed). |
| `Specifications/AlreadyImportedSpecification.cs` | BL-01 fix — queries `IChapterHistoryService.FindByChapterId` (NOT `IHistoryService.FindByEpisodeId`). |

## Patterns / Conventions

### Auto-discovered specs via DryIoc IEnumerable<> ctor injection

```csharp
public MangaImportDecisionMaker(IEnumerable<IMangaImportDecisionEngineSpecification> specifications, Logger logger)
{
    _specifications = specifications;   // DryIoc resolves to all 6 spec impls in this directory
    _logger = logger;
}
```

Adding a new manga import spec is: (1) implement the interface, (2) ship the file under `Specifications/`. No manual DI registration. Mirrors Phase 5 Manga Decision Engine spec auto-discovery (Phase 4 LEARNINGS S1 pattern).

### Pitfall 4 ordering invariant on success path

**RESEARCH §Pitfall 4** — `ChapterImportedEvent` MUST be the LAST line in the per-decision success path. Komga/Kavita rescan handlers (Plans 06-10/11) race-fire on this event; if it publishes before the DB commit + filesystem move complete, the rescan finds no new file and reports "0 new files" — silently broken pipeline.

```csharp
// 1. Build destination path
var destinationPath = _pathBuilder.BuildChapterPath(lc.Manga, lc.Chapter, lc.Path);

// 2. Move staging CBZ → library
_diskProvider.MoveFile(lc.Path, destinationPath);

// 3. ChapterFile DB COMMIT
chapterFile = _chapterFileService.Add(chapterFile);

// 3.5. ImageSharp probe (UpdateChapterInfoService) + ComicInfoCbzInjector — see MediaFiles/CLAUDE.md
// 4. Wire Chapter.ChapterFileId FK
lc.Chapter.ChapterFileId = chapterFile.Id;
_chapterService.UpdateChapter(lc.Chapter);

// 5. Pitfall 4 GUARD — PublishEvent is the LAST line
//    (the former Phase-4 ChapterDownloadState row delete was removed in Phase 39 RETIRE-01 —
//     the in-process downloader no longer creates state rows, so there is nothing to delete)
_eventAggregator.PublishEvent(new ChapterImportedEvent { ... });
```

Per-decision `try/catch` isolation: any exception path publishes `ChapterImportFailedEvent` instead so the auto-retry orchestrator (Plan 06-08) sees the failure. The unit-test `should_not_publish_chapter_imported_event_when_db_commit_fails` is the regression guard for this invariant.

### D-10 three-state effective-upgrade-allowed

`UpgradeSpecification` resolves the per-Manga upgrade gate as:

```csharp
effectiveUpgradeAllowed =
    manga.UpgradeAllowedOverride
    ?? (translationProfile.UpgradeAllowed && customFormatProfile.UpgradeAllowed)
```

| `manga.UpgradeAllowedOverride` | Behavior |
|--------------------------------|----------|
| `null` (default) | Fall back to per-profile flags AND-merged. With Phase 5/6 defaults (`TranslationProfile.UpgradeAllowed=true` + `CustomFormatProfile.UpgradeAllowed=false`), this resolves to **false** out of the box. |
| `true` | Force-allow upgrades on this Manga (per-profile flags ignored). |
| `false` | Force-disallow upgrades (per-profile flags ignored). |

The AND-merge (rather than OR) is intentional: language-rank upgrades are the *arr promise (TranslationProfile defaults TRUE), but CF-score upgrades are subjective (CustomFormatProfile defaults FALSE), so an admin must explicitly opt in to CF-score-driven auto-churn.

After the gate passes, comparison ordering mirrors `MangaDownloadDecisionComparer` D-08: language rank (lower index in `TranslationProfile.Languages` = better) → CF score (higher = better). Existing-file CF score is computed by routing the existing `ChapterFile` through `ICustomFormatCalculationService.ParseCustomFormat(MangaCustomFormatInput)` with a minimal reconstructed `ReleaseInfo` (TranslatedLanguage + ScanlationGroup — the only fields the manga CF specs read per Phase 5 D-09).

### BL-01 cross-domain ID-collision guard

`AlreadyImportedSpecification` queries `IChapterHistoryService.FindByChapterId(chapter.Id)` and matches on `ChapterHistoryEventType.Imported` — NEVER `IHistoryService.FindByEpisodeId(...)`. The Phase 5 STUB version of this code path used the wrong-table query and could silently mark a manga release as "already imported" when an unrelated TV `Episode.Id` happened to share an int value with the `Chapter.Id` (independent SQLite autoincrement sequences).

Plan-checker grep target: `grep -c "FindByEpisodeId" src/NzbDrone.Core/MediaFiles/MangaImport/` returns **0**.

### In-batch dedupe in ImportApprovedChapters

A single batch can contain duplicate decisions for the same `Chapter.Id` (e.g., two staging CBZ files resolved to the same chapter via different parses). The orchestrator tracks seen IDs in `HashSet<int>`; the second occurrence becomes a `Skipped` result with message `"Chapter has already been imported in this batch"` so the caller can surface it without retrying.

## Manga Adaptation Notes

| Mangarr (TV) | Mangarr (manga) |
|-------------|-----------------|
| `LocalEpisode` | `LocalChapter` (drops Quality/MediaInfo/SubtitleInfo/SceneSource/PossibleExtraFiles) |
| `IImportDecisionEngineSpecification` | `IMangaImportDecisionEngineSpecification` |
| `ImportSpecDecision` | `MangaImportSpecDecision` (cached Accept singleton same shape) |
| `ImportRejectionReason` | `ImportRejectionReason` (drops TV-only Sample/SeasonExtra/PartialSeason/NotQualityUpgrade) |
| `ImportDecision` | `MangaImportDecision` |
| `ImportResult` | `MangaImportResult` |
| `ImportDecisionMaker` | `MangaImportDecisionMaker` (drops `IDetectSample` + `IAggregationService` + `ITrackedDownloadService`) |
| `ImportApprovedEpisodes` | `ImportApprovedChapters` (drops `IUpgradeMediaFiles`, `IExtraService`, `IExistingExtraFiles`; adds the step-3.5 ImageSharp probe + ComicInfoCbzInjector. The Phase-4 `ChapterDownloadState` lifecycle hook was removed in Phase 39 RETIRE-01) |
| `IMediaFileService.Add(EpisodeFile)` | `IChapterFileService.Add(ChapterFile)` (Plan 06-01) |
| `ISeriesPathBuilder.BuildPath` | `IBuildMangaPaths.BuildChapterPath` (Plan 06-01) |
| `_historyService.FindByEpisodeId` | `_chapterHistoryService.FindByChapterId` (BL-01 fix; Plan 06-03) |
| `EpisodeImportedEvent` | `ChapterImportedEvent` (Plan 06-02; Pitfall 4 ordering) |
| `EpisodeImportFailedEvent` | `ChapterImportFailedEvent` (Plan 06-02) |
| `EpisodeHistoryEventType.DownloadFolderImported` | `ChapterHistoryEventType.Imported` (manga collapses TV's 3 import flavors) |
| `GrabbedReleaseInfo.EpisodeIds` | `ReleaseInfo.ChapterIds` (Plan 06-07 added; populated at search-time by future Phase 6 dispatchers) |

## Phase 8 cleanup

When `Tv/` deletes (Phase 8 milestone), this directory collapses with `src/NzbDrone.Core/MediaFiles/EpisodeImport/`:

- `MangaImport/Specifications/*` → `EpisodeImport/Specifications/*` (rename `Chapter*` → canonical names)
- `LocalChapter` → `LocalEpisode` becomes `LocalRelease` (or whatever the rename target is)
- `IMangaImportDecisionEngineSpecification` → canonical interface
- `D-10 three-state UpgradeSpecification` stays — it's the manga decision, not a TV port; the TV `UpgradeSpecification` deletes
- `Pitfall 4 ordering invariant` stays as the canonical `ImportApprovedChapters` (post-rename) contract

## Cross-References

- [src/NzbDrone.Core/MediaFiles/EpisodeImport/](../EpisodeImport/) — TV analog
- [src/NzbDrone.Core/MediaFiles/ChapterFile.cs](../ChapterFile.cs) — Plan 06-01 deliverable consumed by `ImportApprovedChapters.Add`
- [src/NzbDrone.Core/Organizer/Manga/MangaPathBuilder.cs](../../Organizer/Manga/MangaPathBuilder.cs) — Plan 06-01 `BuildChapterPath` consumer
- [src/NzbDrone.Core/History/Manga/ChapterHistoryService.cs](../../History/Manga/ChapterHistoryService.cs) — BL-01 fix consumer
- [src/NzbDrone.Core/Profiles/Translations/TranslationProfile.cs](../../Profiles/Translations/TranslationProfile.cs) — D-10 `UpgradeAllowed` flag
- [src/NzbDrone.Core/Profiles/CustomFormats/CustomFormatProfile.cs](../../Profiles/CustomFormats/CustomFormatProfile.cs) — D-10 `UpgradeAllowed` flag
- [src/NzbDrone.Core/Manga/Manga.cs](../../Manga/Manga.cs) — D-10 `UpgradeAllowedOverride : bool?`
