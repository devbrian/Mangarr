# Sonarr.Api.V3

## Purpose

REST API controllers for **API version 3** — the **legacy** API kept for backward compatibility with older clients/scripts that haven't migrated to V5.

Most new development happens in `Sonarr.Api.V5`. **Don't add new endpoints to V3** — those should go to V5.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\Sonarr.Api.V3\`

**File count**: ~156 .cs files across 33 modules.

## URL Prefix

All endpoints prefixed with **`/api/v3`** (configured by the `[V3ApiController]` attribute).

## Differences from V5

V3 is broadly the same surface as V5 with a few differences:
- Some configuration endpoints reorganized (V3 has `Config/` folder, V5 spreads them in `Settings/`)
- Some controllers exist only in V3 (legacy convenience endpoints retained for compat)
- Resource shapes occasionally differ (V5 cleaned up some fields)

### V3-Only Controllers (retained for legacy clients)

| Module | Controllers |
|--------|-------------|
| `AutoTagging/` | `AutoTaggingController` (V5 may handle differently) |
| `Config/` | `ConfigController`, `DownloadClientConfigController`, `HostConfigController`, `ImportListConfigController`, `IndexerConfigController`, `MediaManagementConfigController`, `NamingConfigController`, `UiConfigController` |
| `DownloadClient/` | `DownloadClientController` (V5 calls this `Connections`) |
| `Notifications/` | `NotificationController` |
| `MediaCovers/` | `MediaCoverController` |
| `CustomFormats/` | `CustomFormatController` |

### Common Controllers (mirrored in V5)

`Series/`, `Episodes/`, `EpisodeFiles/`, `Calendar/`, `Wanted/`, `Profiles/`, `Qualities/`, `Tags/`, `RootFolders/`, `Indexers/`, `Blocklist/`, `Queue/`, `History/`, `Release/`, `ManualImport/`, `Commands/`, `System/`, `Health/`, `Logs/`, `Update/`, `ImportLists/`, `Metadata/`, `Parse/`, `FileSystem/`, `Localization/`, `CustomFilters/`, `RemotePathMappings/`, `DiskSpace/`.

## Compatibility Strategy

When adding/changing functionality:

1. **New features** → V5 only
2. **Bug fixes** → Apply to both V3 and V5 if affected
3. **Schema changes** → Add field to V5 resource only; if V3 needs it, add a `[Obsolete("Use V5")]` overload

## Manga Adaptation Notes

V3 will likely be **frozen** during the Sonarr → Mangarr migration:
- New manga endpoints go to V5 (new routes like `/api/v5/manga`).
- V3 may continue to expose `/api/v3/series` if any legacy integrations rely on it; consider returning manga data via the same shape, or eventually deprecate V3 entirely.

If keeping V3 alive long-term, the simplest path is to alias V3 series endpoints to V5 manga endpoints with a translation layer (or vice versa).

## Cross-References

- [PROJECT_CONTEXT.md](../../PROJECT_CONTEXT.md) — Architecture overview
- [Sonarr.Api.V5/CLAUDE.md](../Sonarr.Api.V5/CLAUDE.md) — Current primary API
- [Sonarr.Http/CLAUDE.md](../Sonarr.Http/CLAUDE.md) — Base infrastructure
