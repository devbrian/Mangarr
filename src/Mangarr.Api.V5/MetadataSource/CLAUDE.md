# Sonarr.Api.V5/MetadataSource (Phase 2 Developer Endpoints)

## Purpose

v1 REST CRUD for `IMetadataSource` ThingiProvider definitions + the bespoke `SetPrimary` endpoint per D-15. Per CONTEXT Claude's Discretion: "Phase 7 wires up the React Settings → Metadata Sources page" — Phase 2 ships only the developer surface.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\Sonarr.Api.V5\MetadataSource`

## Key Files

| File | Purpose |
|------|---------|
| `MetadataSourceResource.cs` + Mappers | DTO + bulk DTO; single divergence from IndexerResource is the `IsPrimary` bool |
| `MetadataSourceController.cs` | `ProviderControllerBase` (Get/Post/Put/Delete/Test/Schema/TestAll/Action) + bespoke `[HttpPost("{id}/setprimary")]` route |

## Endpoints

- `GET    /api/v5/metadatasource` — list all configured metadata sources
- `GET    /api/v5/metadatasource/{id}` — get one
- `POST   /api/v5/metadatasource` — create
- `PUT    /api/v5/metadatasource/{id}` — update
- `DELETE /api/v5/metadatasource/{id}` — delete
- `POST   /api/v5/metadatasource/test` — validate Settings (calls `IMetadataSource.Test()`)
- `GET    /api/v5/metadatasource/schema` — return ProviderDefinition schema for UI form generation
- `POST   /api/v5/metadatasource/{id}/setprimary` — promote to `IsPrimary=true`; demote all others atomically (D-15)
- `POST   /api/v5/metadatasource/testall` — TestAll across configured sources
- `POST   /api/v5/metadatasource/action/{name}` — RequestAction passthrough
- `PUT    /api/v5/metadatasource/bulk` / `DELETE /api/v5/metadatasource/bulk` — bulk operations (tags only — IsPrimary intentionally NOT bulk-applicable per D-15 invariant)

## Patterns / Conventions

- All endpoints `[V5ApiController]` → admin X-Api-Key requirement (RESEARCH §Security Domain V4)
- `ProviderControllerBase` auto-provides the standard ThingiProvider REST surface — same shape as `IndexerController`
- `SetPrimary(id)` delegates to `MetadataSourceFactory.SetPrimary` which enforces D-15's at-most-one invariant via demote-all-then-promote-one
- Settings JSON is the inherited `ProviderDefinition.Settings` storage; ClientId (MAL) is stored in plaintext per RESEARCH §Security Domain (same posture as Sonarr indexer API keys — local admin-only application)
- Threat T-CONFIG-DRIFT-01 mitigation: the route handler never mutates `IsPrimary` directly; it goes through the factory invariant gate

## Manga Adaptation Notes

- Phase 7 wires the React Settings → Metadata Sources page against this controller; Add Metadata Source flow uses the schema endpoint to render the Settings form, then POSTs the resource. The "Set as Primary" button hits `setprimary`.
- v2 metadata sources (e.g., MangaUpdates) plug in via the standard ThingiProvider auto-discovery without controller changes.

## Cross-References

- Sonarr V5 analog: [`src/Sonarr.Api.V5/Indexers/`](../Indexers/) (`IndexerController`, `IndexerResource`)
- Factory + invariant: [`src/NzbDrone.Core/MetadataSource/CLAUDE.md`](../../NzbDrone.Core/MetadataSource/CLAUDE.md), [`MetadataSourceFactory.cs`](../../NzbDrone.Core/MetadataSource/MetadataSourceFactory.cs)
- Provider base: [`src/Sonarr.Api.V5/Provider/ProviderControllerBase.cs`](../Provider/ProviderControllerBase.cs)
