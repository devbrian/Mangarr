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
