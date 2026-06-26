# Settings/

## Purpose

All user-configurable settings UI. Each subdirectory is one settings page (or section). 273+ files — the largest feature module after `Components/`.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\src\Settings\`

## Top-Level Files

| File | Purpose |
|------|---------|
| `Settings.tsx` | Settings home / hub page |
| `SettingsToolbar.tsx` | Save/Cancel toolbar visible on each settings page |
| `AdvancedSettingsButton.tsx` | "Show advanced" toggle |
| `PendingChangesModal.tsx` | "You have unsaved changes" warning |
| `advancedSettingsStore.ts` | Zustand — advanced-mode toggle |
| `useProviderSchema.ts` | Fetch the dynamic UI form schema for a provider (indexer, dl-client, notification) |
| `useProviderSettings.ts` / `useProviderOptions.ts` | Helpers for editing provider configs |
| `useSettings.ts` | General settings hook |

## Subdirectories (one per page)

| Folder | Page | Route |
|--------|------|-------|
| `General/` | General host/port/proxy/auth/SSL/log/backup config | `/settings/general` |
| `MediaManagement/` | File handling, naming, root folders | `/settings/mediamanagement` |
| `Profiles/` | Page renders the `TranslationProfiles` editor (nav label "Translation Profiles"). The `Profiles/{Quality,Delay,Release}/` legacy sub-trees remain on disk, unlinked from the nav. | `/settings/profiles` |
| `Profiles/Translations/` | TranslationProfile editor wiring `/api/v5/translationprofile`. | (rendered inside `/settings/profiles`) |
| `Profiles/CustomFormatProfile/` | CustomFormatProfile editor wiring `/api/v5/customformatprofile`. | `/settings/customformatprofiles` |
| `CustomFormats/` | Custom format CRUD + specifications | `/settings/customformats` |
| `Indexers/` | Indexers + global options | `/settings/indexers` |
| `DownloadClients/` | Download clients + remote path mappings | `/settings/downloadclients` |
| `ImportLists/` | Import lists + exclusions + options | `/settings/importlists` — **Phase 26 Plan 26-05 closure**: Phase 15 stub Alert rewritten as translated tree (Indexers-shape mirror per RESEARCH §Q7). Wires `ImportLists/` provider list + Add/Edit modals + `ManageImportListsModal` (substrate-shell — Phase 27 expands) + `ImportListExclusions/` paged list + `Options/ImportListOptions.tsx` (notice-only — no backing `/api/v5/settings/importlist` controller in substrate). D-05 enforced: no Test-All / Sync-Now button. Pattern κ verified: zero `series-*` / `episode-*` / `season-*` / `add-series-*` testids. |
| `Notifications/` | Notification providers | `/settings/connect` |
| `Metadata/` | Metadata writers (NFO etc.) | `/settings/metadata` |
| `MetadataSource/` | Metadata source config (TVDB → manga sources) | `/settings/metadatasource` |
| `Tags/` | Tags + auto-tagging | `/settings/tags` — **Phase 24 Plan 24-04 closure**: `Settings/Tags/AutoTagging/` was already shipped at the FE layer pre-Phase-24 but the Redux thunks at `Store/Actions/Settings/autoTaggings.js` 404'd because no V5 controller answered. Phase 24-04 shipped the V5 controllers + TagController `IHandle<AutoTagsUpdatedEvent>` re-wire — the surface is now live with NO FE code changes (Redux thunk path resolution alone). Rule-creation modal + 11-spec dropdown + RemoveTagsAutomatically toggle all functional. |
| `UI/` | Theme, language, time format | `/settings/ui` |

## Profiles topology (Phase 7 D-05 → Phase 15 D-12)

- The `/settings/profiles` page (`Settings/Profiles/Profiles.tsx`) renders `<TranslationProfiles />` only; nav label is "Translation Profiles".
- A separate "Custom Format Profiles" nav row routes to `/settings/customformatprofiles`.
- The old top-level `Quality` settings page is gone: the `/settings/quality` route and the top-level `frontend/src/Quality/` sub-tree were **deleted in Phase 15 D-12** (see the divergence comments in `Settings.tsx`). The `Settings/Profiles/{Quality,Delay,Release}/` sub-trees still exist on disk but are unlinked from the nav.

## Provider Settings Pattern

Several settings pages share a common pattern: they show a list of **provider instances** (Indexers, Download Clients, Notifications, Import Lists), each editable via a modal whose form is built dynamically from a JSON schema sent by the backend.

### Flow
1. User clicks "Add" on a settings page (e.g., `IndexerSettings`)
2. UI shows a list of provider **types** (Newznab, Torznab, Nyaa, etc.)
3. User picks a type
4. Frontend calls `useProviderSchema('indexer', selectedImplementation)` → returns field schema
5. Form built from schema using `Components/Form/FormInputGroup` (text, password, select, etc.)
6. Save → POST/PUT to backend → backend persists `IndexerDefinition`

This pattern is implemented once in shared components and reused by all four areas. See:
- `useProviderSchema.ts` — fetches schema
- `useProviderSettings.ts` — load existing settings
- `useProviderOptions.ts` — load options sets

The backend builds these schemas via `Mangarr.Http/ClientSchema/SchemaBuilder.cs` (reflects on the C# settings class).

## Page Structure (Conventional)

```
SettingsPageName/
├── PageName.tsx                    # Page-level component
├── Edit/                           # Edit modal
├── Add/                            # Add modal (often type picker → edit)
├── Manage/                         # Bulk manage modal
└── (subdirectories per provider)
```

## Form Components

Most settings forms use shared form components from `frontend/src/Components/Form/`:
- `Form` / `FormGroup` / `FormLabel` / `FormInputGroup`
- Inputs: `TextInput`, `NumberInput`, `CheckInput`, `SelectInput`, `PasswordInput`, `EnhancedSelectInput`, `KeyValueListInput`, `TagInput`, `PathInput`, `RootFolderSelectInput`, `QualityProfileSelectInput`, `IndexerSelectInput`, `DownloadClientSelectInput`, etc.

## Manga Adaptation Notes

Settings pages are largely **architecture-stable** — provider plugin model means new manga indexers / download clients / etc. show up automatically once the backend exposes them.

| Page | Manga Adaptation |
|------|------------------|
| `Profiles/` | TranslationProfile (ordered BCP-47 language list) replaces TV quality items |
| `CustomFormats/` | Specs adapt (page count, scanlation group, etc.) |
| `Indexers/` | Just list the new manga indexers (auto from backend) |
| `Metadata/` | Metadata writers (NFO, banner.jpg) — adapt to manga-specific files |
| `MetadataSource/` | Full ThingiProvider list page (debug-session 2026-05-10, GH #49) — Add/Edit/Delete + D-15 `Set as Primary` button mirroring `Settings/Notifications/Notifications/` shape |
| `MediaManagement/` | Naming tokens change (chapter/volume rather than season/episode) |
| `Notifications/` | List unchanged; per-provider message text changes |
| Others (UI, Tags, General) | Reusable as-is |

## Cross-References

- [../../CLAUDE.md](../../CLAUDE.md) — Frontend overview
- [../Components/CLAUDE.md](../Components/CLAUDE.md) — Form components
- [../../../src/Mangarr.Http/CLAUDE.md](../../../src/Mangarr.Http/CLAUDE.md) — `ClientSchema` builds form schemas
- [../../../src/NzbDrone.Core/Profiles/CLAUDE.md](../../../src/NzbDrone.Core/Profiles/CLAUDE.md) — Backend for Profiles page
- [../../../src/NzbDrone.Core/CustomFormats/CLAUDE.md](../../../src/NzbDrone.Core/CustomFormats/CLAUDE.md) — Backend for CustomFormats page

## Phase History Footnotes

- **Phase 26 Plan 26-05 (IL-14 / Pitfall 14)** — `useProviderOptions.ts:77` `/api/v3/{provider}/action/{action}` → `/api/v5/{provider}/action/{action}` one-line path fix. Stale V3 URL was a hold-over from the pre-Mangarr-v5 fork; the V5 provider action endpoint inherited from `ProviderControllerBase.RequestAction` (route `[HttpPost("action/{name}")]`) is the canonical Mangarr surface. Phase 27 provider dynamic-options dropdowns consume this hook directly.
- **Phase 26 Plan 26-05 (Pitfall 15 verification-only)** — `frontend/src/Activity/Queue/Status/useQueueStatus.ts:32` independently re-verified as `/manga/queue/status` (matches `MangaQueueStatusController.cs:21-30` shape). Already correct from Phase 13 Plan 13-09 close-out; Plan 26-05 did NOT modify the file.
