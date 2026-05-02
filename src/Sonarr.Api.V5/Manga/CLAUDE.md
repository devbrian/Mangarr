# Sonarr.Api.V5/Manga (Phase 2 Developer Endpoints)

## Purpose

v1 developer REST endpoints for Manga + Lookup + Links. Per CONTEXT: "Phase 2 ships these endpoints only as developer surfaces — UI lives in Phase 7." All endpoints carry `[V5ApiController]` (admin X-Api-Key requirement per RESEARCH §Security Domain V4).

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\Sonarr.Api.V5\Manga`

## Key Files

| File | Purpose |
|------|---------|
| `MangaResource.cs` + `MangaResourceMapper` | DTO with singular cross-source IDs (`Guid? MangaDexId`, `int? MalId`, `int? AniListId`) per CONTEXT specifics |
| `MangaController.cs` | GET/POST/PUT/DELETE `/api/v5/manga` (RestControllerWithSignalR) |
| `MangaLookupController.cs` | GET `/api/v5/manga/lookup?term=` — META-01 (uses primary metadata source via `IMetadataSourceFactory.GetPrimary`) |
| `MangaLinksController.cs` | POST `/api/v5/manga/{id}/links` — manual relink per D-23 (NO auto-validation against secondary sources) |

## Endpoints

- `GET    /api/v5/manga` — list all manga (covers mapped to local URLs)
- `GET    /api/v5/manga/{id}` — get one manga
- `POST   /api/v5/manga` — add a manga (delegates to `IAddMangaService.AddManga`; requires at least one of MangaDexId/MalId/AniListId)
- `PUT    /api/v5/manga/{id}` — update mutable fields via `Manga.ApplyChanges` (canonical IDs immutable here per Plan 02-09)
- `DELETE /api/v5/manga/{id}` — delete (optionally with `?deleteFiles=true`)
- `GET    /api/v5/manga/lookup?term=` — META-01 search via primary metadata source
- `POST   /api/v5/manga/{id}/links` — manual relink per D-23 (BYPASSES `CrossSourceIdResolver`)

## Patterns / Conventions

- All endpoints `[V5ApiController]` → admin X-Api-Key requirement (RESEARCH §Security Domain V4 — threat T-AUTHN-01)
- `MangaController` uses `RestControllerWithSignalR<MangaResource, Manga>` and `IHandle`s the 4 manga events (`MangaAddedEvent`, `MangaUpdatedEvent`, `MangaDeletedEvent`, `ChapterListUpdatedEvent`) to broadcast updates to SignalR clients
- `MangaLookupController` resolves the primary metadata source at REQUEST time via `IMetadataSourceFactory.GetPrimary` (per D-15 dynamic primary — user can change primary without restart)
- `MangaLinksController` BYPASSES `CrossSourceIdResolver` per D-23 — user-supplied IDs are accepted verbatim. Auto-validation only runs at add-time and on refresh.
- Singular cross-source IDs (`Guid?`, `int?`) on `MangaResource` reflect the manga 1:1-across-sources model (vs anime's pluralized HashSets on `Series`)

## Manga Adaptation Notes

- Phase 7 wires React UI against these endpoints (Add Manga page → `/lookup` + POST `/manga`; Settings → Metadata Sources → `/metadatasource`; Manga detail page → POST `/manga/{id}/links` button)
- Phase 8 rename: when `Series → Manga` cutover lands, this directory becomes the canonical "primary domain" controller and the existing `Series/` peers are deleted

## Cross-References

- Sonarr V5 analogs: [`src/Sonarr.Api.V5/Series/`](../Series/) (`SeriesController`, `SeriesLookupController`, `SeriesResource`)
- Manga model + services: [`src/NzbDrone.Core/Manga/CLAUDE.md`](../../NzbDrone.Core/Manga/CLAUDE.md)
- AddMangaService (META-02 orchestrator, D-19/D-20 wiring): [`src/NzbDrone.Core/Manga/AddMangaService.cs`](../../NzbDrone.Core/Manga/AddMangaService.cs)
- MetadataSource scaffold (D-14, D-15): [`src/NzbDrone.Core/MetadataSource/CLAUDE.md`](../../NzbDrone.Core/MetadataSource/CLAUDE.md)
- Manga cover mapper (Plan 02-09 sibling per RESEARCH §Pattern 5): [`src/NzbDrone.Core/MediaCover/MangaMediaCoverService.cs`](../../NzbDrone.Core/MediaCover/MangaMediaCoverService.cs)
