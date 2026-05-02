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
| `Profiles/` | Quality + language + delay + release profiles | `/settings/profiles` |
| `Quality/` | Quality definitions (size limits) | `/settings/quality` |
| `CustomFormats/` | Custom format CRUD + specifications | `/settings/customformats` |
| `Indexers/` | Indexers + global options | `/settings/indexers` |
| `DownloadClients/` | Download clients + remote path mappings | `/settings/downloadclients` |
| `ImportLists/` | Import lists + exclusions + options | `/settings/importlists` |
| `Notifications/` | Notification providers | `/settings/connect` |
| `Metadata/` | Metadata writers (NFO etc.) | `/settings/metadata` |
| `MetadataSource/` | Metadata source config (TVDB → manga sources) | `/settings/metadatasource` |
| `Tags/` | Tags + auto-tagging | `/settings/tags` |
| `UI/` | Theme, language, time format | `/settings/ui` |

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

The backend builds these schemas via `Sonarr.Http/ClientSchema/SchemaBuilder.cs` (reflects on the C# settings class).

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
| `Quality/` | Replace TV resolution definitions with manga quality tiers. UI structure unchanged. |
| `Profiles/` | Quality items list adapts; rest is identical |
| `CustomFormats/` | Specs adapt (page count, scanlation group, etc.) |
| `Indexers/` | Just list the new manga indexers (auto from backend) |
| `Metadata/` | Metadata writers (NFO, banner.jpg) — adapt to manga-specific files |
| `MetadataSource/` | New — choose primary manga source (MangaDex / AniList / MAL) |
| `MediaManagement/` | Naming tokens change (chapter/volume rather than season/episode) |
| `Notifications/` | List unchanged; per-provider message text changes |
| Others (UI, Tags, General) | Reusable as-is |

## Cross-References

- [../../CLAUDE.md](../../CLAUDE.md) — Frontend overview
- [../Components/CLAUDE.md](../Components/CLAUDE.md) — Form components
- [../../../src/Sonarr.Http/CLAUDE.md](../../../src/Sonarr.Http/CLAUDE.md) — `ClientSchema` builds form schemas
- [../../../src/NzbDrone.Core/Profiles/CLAUDE.md](../../../src/NzbDrone.Core/Profiles/CLAUDE.md) — Backend for Profiles page
- [../../../src/NzbDrone.Core/CustomFormats/CLAUDE.md](../../../src/NzbDrone.Core/CustomFormats/CLAUDE.md) — Backend for CustomFormats page
