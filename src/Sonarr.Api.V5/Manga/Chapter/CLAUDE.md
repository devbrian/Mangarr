# Manga/Chapter (V5 API)

## Purpose

Top-level `/api/v5/chapter` REST surface — per-chapter monitor toggle + manual search.
Sonarr divergence: NEW manga sibling of `src/Sonarr.Api.V5/Episodes/`. Phase 8 cleanup
collapses with `EpisodeController` when `Tv/` deletes.

This is the single backend endpoint Phase 7 ships. Phase 2/5/6 already shipped every
other manga endpoint (`/api/v5/manga`, `/api/v5/manga/lookup`, `/api/v5/manga/links`,
`/api/v5/translationprofile`, `/api/v5/customformatprofile`,
`/api/v5/config/manga-naming`, `/api/v5/manga/queue`, `/api/v5/manga/history`,
`/api/v5/manga/blocklist`, `/api/v5/manga/release`, `/api/v5/manga/wanted/missing`).

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\Sonarr.Api.V5\Manga\Chapter`

## Key Files

| File | Purpose |
|------|---------|
| `ChapterController.cs` | GET / PUT / POST endpoints. Bare `[V5ApiController]` auto-derives the SignalR resource name `chapter` from `ChapterResource.ResourceName` (Pitfall 2 + Lock #6). |
| `ChapterResource.cs` | DTO + `ChapterResourceMapper.ToResource` extension (single + IEnumerable). |
| `ChaptersMonitoredResource.cs` | Bulk `PUT /monitor` body shape: `{ ChapterIds: List<int>, Monitored: bool }`. |

## Endpoints

```
GET    /api/v5/chapter?mangaId={id}        List chapters for a manga (D-07)
GET    /api/v5/chapter?chapterIds=1,2,3    List specific chapters
GET    /api/v5/chapter/{id}                Get one (404 on missing)
PUT    /api/v5/chapter/{id}                Single-row monitor flip ({ Monitored: bool } body)
PUT    /api/v5/chapter/monitor             Bulk monitor flip ({ ChapterIds, Monitored } body)
POST   /api/v5/chapter/{id}/search         Enqueue ChapterSearchCommand for the chapter
```

`GET /api/v5/chapter` with neither `mangaId` nor `chapterIds` returns **400 BadRequest**
(T-07-01 mitigation, mirrors `EpisodeController.GetEpisodes` precedent).

## Patterns / Conventions

- **Bare `[V5ApiController]`** (NOT `[V5ApiController("chapter")]`) → auto-derives both
  the HTTP route `/api/v5/chapter` AND the SignalR resource name `chapter` (the latter
  via `RestControllerWithSignalR`'s reflection on `ChapterResource.ResourceName`). Per
  Pitfall 2 + Lock #6 — overriding the resource string would fork the SignalR push
  channel name from the React handler entry.
- **`[Produces("application/json")]` on every HTTP method** (Pitfall 4) so the OpenAPI
  v5 doc registers the response schema for each endpoint.
- **Per-chapter search dispatches `ChapterSearchCommand`** (Phase 6 D-12) via
  `IManageCommandQueue.Push` with `CommandPriority.Normal` + `CommandTrigger.Manual`.
  T-07-04 acceptance: `Push` dedups by command equality; a click-flood collapses to
  one queued command.
- **SignalR push via `IHandle<ChapterUpdatedEvent>` → `BroadcastResourceChange`.** The
  event is published by `ChapterService.SetChapterMonitored` and
  `ChapterService.SetChaptersMonitored` (Plan 07-01 Task 1), AFTER the DB write per
  Pitfall 4 ordering invariant.
- **`NotFoundException` on missing id** so the framework maps to 404 (mirrors
  `MangaController.GetResourceById` precedent — without the explicit throw the
  framework returns HTTP 200 with a null body).

## Manga Adaptation Notes

Manga sibling diverges from `EpisodeController`:

- No `seasonNumber` filter (PROJECT.md "Volumes/Seasons" Out-of-Scope row).
- No `EpisodeFile` / `Series` subresource (deferred until a real consumer needs it; the
  React detail page reads chapter + manga separately and assembles client-side).
- POST `{id}/search` endpoint is NEW (no `EpisodeController` peer) — D-07 ships this
  alongside the GET/PUT shape.
- `ChapterResource` adds `TranslatedLanguage` (BCP-47), `ScanlationGroup`, `IsSynthetic`,
  `ChapterType` (string projection of the enum), `VolumeNumber` (display-only — no
  Volumes table).

## Phase 13 — API V5 surface backfill

Phase 13 (API V5 Surface Audit) added two new controllers under this directory per the
sub-wave A FINDINGS sweep ([13-API-V5-SURFACE-FINDINGS.md](../../../../.planning/phases/13-api-v5-surface-audit/13-API-V5-SURFACE-FINDINGS.md))
under the D-13-04 forward-prophylactic + D-13-07 Series-rename family rule disposition.
Both are additive to the pre-existing `ChapterController` (Phase 7 Plan 07-01).

### Key Files (Phase 13 additions)

| File | Purpose |
|------|---------|
| `RenameChapterController.cs` + `RenameChapterResource.cs` | Rename-preview at `/api/v5/manga/rename` + `/api/v5/manga/rename/bulk` (Plan 13-06 — D-13-04 forward-prophylactic). Bare `Controller` (read-only on-demand, no SignalR contract). Resource Id maps to `ChapterFileId` (mirrors TV peer `RenameEpisodeResource.Id = EpisodeFileId`). |
| `ChapterFileController.cs` + `ChapterFileResource.cs` + `ChapterFileListResource.cs` | CRUD + SignalR fan-out at `/api/v5/ChapterFile` (Plan 13-07 — D-13-04 forward-prophylactic). `RestControllerWithSignalR<ChapterFileResource, ChapterFile>` + `IHandle<ChapterFileAddedEvent>` + `IHandle<ChapterFileDeletedEvent>`. SignalR resource auto-derived = `chapterfile` (lowercase per `RestResource.cs:11` `GetType().Name.ToLowerInvariant().Replace("resource", "")`). |
| `ChapterController.cs` (Phase 7 Plan 07-01 — pre-existing, NOT Phase 13) | GET / PUT / POST `/api/v5/chapter` (RestControllerWithSignalR; auto-derived `chapter` resource). Listed here for completeness. |

### Endpoints (Phase 13 additions)

```
GET    /api/v5/manga/rename?mangaId={id}&chapterNumber={decimal?}   Rename preview (1 or 2-arg overload; chapterNumber is decimal? per Phase 2 D-12, NOT int? like TV peer)
GET    /api/v5/manga/rename/bulk?mangaIds={ids}                     Bulk rename preview (positive-int + non-empty validation throws BadRequestException — T-13-05 mitigation)

GET    /api/v5/ChapterFile/{id}                                     Get one chapter file
GET    /api/v5/ChapterFile?mangaId={id}                             List by parent manga (delegates to IChapterFileService.GetFilesByManga)
GET    /api/v5/ChapterFile?chapterFileIds={ids}                     List by ids (delegates to IChapterFileService.Get(IEnumerable<int>))
DELETE /api/v5/ChapterFile/{id}                                     Delete one (RestDeleteById; recycles via IDeleteMediaFiles.DeleteChapterFile per Pitfall 4 ordering invariant)
DELETE /api/v5/ChapterFile/bulk                                     Bulk delete (body: { chapterFileIds: [...] } via ChapterFileListResource)
```

PUT (single + bulk) intentionally OMITTED on `ChapterFileController` — the manga DTO has
no metadata fields to flip (Phase 5 D-05: no QualityModel, no SceneName, no IndexerFlags,
no ReleaseType). The CF + Translation Profile cutoff lives on `MangaCutoffController`
(Phase 12). If a future consumer needs per-file metadata edits, add `[RestPutById] SetMetadata`
mirroring the TV shape.

### SignalR resource bindings

| Controller | Resource name | Source | Frontend handler |
|------------|---------------|--------|-------------------|
| `ChapterController` (Phase 7 Plan 07-01) | `chapter` | Auto-derived from `ChapterResource.ResourceName` | `SignalRListener.tsx` `name === 'chapter'` against `['/chapter']` (Plan 07-02) |
| `ChapterFileController` (Phase 13 Plan 13-07) | `chapterfile` (LOWERCASE) | Auto-derived from `ChapterFileResource.ResourceName` per `RestResource.cs:11` | `SignalRListener.tsx` `name === 'chapterfile'` against `['/chapterFile']` (camelCase URL) — Plan 13-07 Task 3 closes Plan 13-00 Pattern κ orphan-flag |

**Critical:** the SignalR token `chapterfile` is **lowercase** (case-sensitive `===` match in
JavaScript). A future regression that adds an explicit `[V5ApiController("chapterFile")]`
camelCase override would silently break the frontend handler match — `ChapterFileControllerFixture`
test `ChapterFileResource_ResourceName_yields_lowercase_chapterfile_signalr_token` pins this
contract before the smoke test would surface the symptom. The React Query key `['/chapterFile']`
is camelCase (matches the actual auto-derived HTTP route `/api/v5/ChapterFile`).

### Patterns / Conventions (Phase 13 additions)

- **`RenameChapterController` extends bare `Controller`** (NOT `RestControllerWithSignalR<,>`) per RESEARCH §Pitfall 1 — read-only on-demand endpoint with no SignalR push contract. Mirror of TV peer `RenameEpisodeController.cs:11` verbatim.
- **`RenameChapterController` route literal `manga/rename`** (NOT bare `rename` like TV peer) per D-13-07 Series-rename family rule + Phase 7 D-09 additive-route convention — keeps manga endpoints under the `/api/v5/manga/*` namespace.
- **`ChapterFileController` uses bare `[V5ApiController]`** (NOT explicit-string override) — auto-derives BOTH the HTTP route `/api/v5/ChapterFile` AND the SignalR resource name `chapterfile` (Pitfall 2 + Lock #6 + Plan 13-00 Pattern κ).
- **`IDeleteMediaFiles` extended (NOT manga-specific `IDeleteChapterFiles`)** — `DeleteChapterFile(Manga, ChapterFile)` added per Plan 13-07 Rule 2; keeps the per-file deletion seam unified across TV and manga so Phase 8 cleanup just removes the `DeleteEpisodeFile` method instead of merging two interfaces. Same Pitfall 4 recycle-then-DB-delete ordering as `DeleteEpisodeFile`.
- **Bulk endpoint validation throws `BadRequestException`** (`RenameChapterController.GetChapters` bulk overload) — mirrors TV peer `RenameEpisodeController.cs:36-44` verbatim; T-13-05 mitigation. Also applied on `ChapterFileController` GET when neither `mangaId` nor `chapterFileIds` are supplied.
- **Phase 15 collapse:** Both new controllers collapse with their TV peers (`RenameEpisodeController` and `EpisodeFileController`) into a single canonical `RenameController` / `FileController` when `Tv/` deletes per D-13-16. The `chapterfile` SignalR resource name collapses to `file` (or whatever the unified name lands on).

### Cross-References (Phase 13)

- Sub-wave A FINDINGS (canonical source of truth): [13-API-V5-SURFACE-FINDINGS.md](../../../../.planning/phases/13-api-v5-surface-audit/13-API-V5-SURFACE-FINDINGS.md)
- Phase 13 plan SUMMARYs:
  - [Plan 13-06 SUMMARY](../../../../.planning/phases/13-api-v5-surface-audit/13-06-SUMMARY.md) — `RenameChapterController` + `RenameChapterResource`
  - [Plan 13-07 SUMMARY](../../../../.planning/phases/13-api-v5-surface-audit/13-07-SUMMARY.md) — `ChapterFileController` + `ChapterFileResource` + `IDeleteMediaFiles.DeleteChapterFile` extension + `SignalRListener.tsx` `chapterfile` handler
- Frontend SignalR handler enumeration: [`frontend/src/Components/CLAUDE.md`](../../../../frontend/src/Components/CLAUDE.md) (SignalRListener section)

## Cross-References

- [V5 API root](../../CLAUDE.md)
- [Episodes (Sonarr analog)](../../Episodes/) — verbatim shape source for
  `EpisodeController.cs` + `EpisodeResource.cs` + `EpisodesMonitoredResource.cs`
- [Manga V5 root](../CLAUDE.md) — Phase 2 + Phase 6 sibling endpoints
- [`IChapterService`](../../../NzbDrone.Core/Manga/IChapterService.cs) — Plan 07-01
  Task 1 added the `SetChaptersMonitored(IEnumerable<int>, bool)` bulk overload
- [`ChapterUpdatedEvent`](../../../NzbDrone.Core/Manga/Events/ChapterUpdatedEvent.cs) —
  NEW manga event sibling driving the SignalR `chapter` push (Plan 07-01 Task 1)
- [`ChapterSearchCommand`](../../../NzbDrone.Core/IndexerSearch/Manga/ChapterSearchCommand.cs) —
  Phase 6 D-12 single-element-list shape consumed by POST `{id}/search`
