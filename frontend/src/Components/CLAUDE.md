# Components (Shared UI)

## Purpose

Reusable UI components used across the application. These are **generic and can be reused as-is** for Mangarr.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\src\Components\`

## Directory Structure

```
Components/
├── Page/                    # Page layout
│   ├── Page.tsx
│   ├── PageContent.tsx
│   ├── PageContentBody.tsx
│   ├── Toolbar/             # Toolbar components
│   ├── Header/              # Page header
│   └── Sidebar/             # Navigation sidebar
├── Table/                   # Table components
│   ├── Table.tsx
│   ├── TableBody.tsx
│   ├── TableHeader.tsx
│   ├── TableHeaderCell.tsx
│   ├── TableRow.tsx
│   ├── TableRowCell.tsx
│   └── VirtualTable*.tsx    # Virtual scrolling variants
├── Form/                    # Form components
│   ├── FormGroup.tsx
│   ├── FormLabel.tsx
│   ├── FormInputGroup.tsx
│   └── various inputs...
├── Modal/                   # Modal dialogs
│   ├── Modal.tsx
│   └── ModalContent.tsx
├── Alert/                   # Alert messages
├── Card/                    # Card containers
├── Icon/                    # Icon component
├── Label/                   # Labels/badges
├── Link/                    # Link components
├── Loading/                 # Loading indicators
├── Menu/                    # Dropdown menus
├── Filter/                  # Filter UI
├── FileBrowser/             # File browser
├── ProgressBar.tsx          # Progress bar
├── CircularProgressBar.tsx  # Circular progress
├── MonitorToggleButton.tsx  # Monitor toggle
├── HeartRating.tsx          # Rating display
├── DescriptionList/         # Key-value lists
├── FieldSet/                # Fieldset container
└── Error/                   # Error displays
```

## Key Components

### ProgressBar
```typescript
<ProgressBar
  progress={75}
  kind="primary"    // primary, success, warning, danger
  showText={true}
  text="75%"
/>
```

### MonitorToggleButton
```typescript
<MonitorToggleButton
  monitored={true}
  isDisabled={false}
  onPress={() => toggleMonitor()}
/>
```

### Table
```typescript
<Table>
  <TableHeader>
    <TableHeaderCell>Title</TableHeaderCell>
  </TableHeader>
  <TableBody>
    <TableRow>
      <TableRowCell>Content</TableRowCell>
    </TableRow>
  </TableBody>
</Table>
```

### Modal
```typescript
<Modal isOpen={isOpen} onClose={onClose}>
  <ModalContent onClose={onClose}>
    {/* Modal content */}
  </ModalContent>
</Modal>
```

### Page Layout
```typescript
<Page>
  <PageToolbar>
    {/* Toolbar buttons */}
  </PageToolbar>
  <PageContent>
    <PageContentBody>
      {/* Main content */}
    </PageContentBody>
  </PageContent>
</Page>
```

## Form Components

| Component | Purpose |
|-----------|---------|
| `TextInput` | Text input field |
| `NumberInput` | Numeric input |
| `CheckInput` | Checkbox |
| `SelectInput` | Dropdown select |
| `PathInput` | File path input |
| `TagInput` | Tag selection |
| `QualityProfileSelectInput` | Quality profile picker |

## Icon Usage

```typescript
import { icons } from 'Helpers/Props';

<Icon name={icons.REFRESH} />
<Icon name={icons.SEARCH} />
<Icon name={icons.EDIT} />
<Icon name={icons.DELETE} />
```

## CSS Modules

All components use CSS modules:
```typescript
import styles from './ProgressBar.module.css';

<div className={styles.container}>
  <div className={styles.progress} />
</div>
```

## Reusability

These components are **domain-agnostic** and require no changes for Mangarr:
- All layout components
- All form components
- Tables, modals, menus
- Progress indicators
- Icons and labels

## SignalRListener (`SignalRListener.tsx`)

Hub-message dispatch table that opens `/signalr/messages?access_token=<apiKey>` on mount and routes per-resource pushes to React Query cache invalidations / Redux store updates / `repopulatePage` calls. Phase 7 Plan 07-02 extends the dispatch table with **6 manga resource handlers** (closes Phase 6 deferred-items.md F-01); Phase 12 follow-up (F-CUTOFF-SIGNALR closure, 2026-05-06) adds a **7th** handler for `manga/wanted/cutoff`; Phase 13 Plan 13-07 (Plan 13-00 Pattern κ closure, 2026-05-07) adds an **8th** handler for `chapterfile` (the SignalR resource auto-derived from `ChapterFileResource.ResourceName`). The Phase 12 F-MISSING-SIGNALR follow-up (2026-05-06) upgrades the existing `manga/wanted/missing` handler from the original `invalidateQueries` shape to the same per-row `updatePagedItem<Episode>` shape after `MangaMissingController` was refactored to extend `RestControllerWithSignalR<MissingChapterResource, Chapter>` for cross-controller consistency with `MangaCutoffController`:

| Resource | Backend `[V5ApiController(...)]` route | Handler shape |
|----------|----------------------------------------|---------------|
| `manga` | `[V5ApiController]` (bare) on MangaController | `updateQueryClientItem` / `removeQueryClientItem` against `['/manga']` (mirrors `series` handler) |
| `chapter` | `[V5ApiController]` (bare) on ChapterController (Plan 07-01) | `updateQueryClientItem` against `['/chapter']` (mirrors `episode` handler) |
| `chapterfile` (lowercase) | `[V5ApiController]` (bare) on ChapterFileController (Phase 13 Plan 13-07) | `updateQueryClientItem(queryClient, ['/chapterFile'], updatedItem, true)` on `updated` action (mirrors `episodefile` handler at lines 157-181 — `addMissing=true` so newly imported files surface) + `repopulatePage('chapterFileUpdated')`; `removeQueryClientItem(queryClient, ['/chapterFile'], id)` on `deleted` action + `repopulatePage('chapterFileDeleted')`. SignalR resource `chapterfile` is **lowercase** (auto-derived via `RestResource.cs:11` `GetType().Name.ToLowerInvariant().Replace("resource", "")`); the React Query key `['/chapterFile']` is **camelCase** (matches the auto-derived HTTP route `/api/v5/ChapterFile`). |
| `manga/queue` | `[V5ApiController("manga/queue")]` on MangaQueueController | `queryClient.invalidateQueries({ queryKey: ['/manga/queue'] })` |
| `manga/blocklist` | `[V5ApiController("manga/blocklist")]` on MangaBlocklistController | `queryClient.invalidateQueries({ queryKey: ['/manga/blocklist'] })` |
| `manga/history` | `[V5ApiController("manga/history")]` on ChapterHistoryController | `queryClient.invalidateQueries({ queryKey: ['/manga/history'] })` |
| `manga/wanted/missing` | `[V5ApiController("manga/wanted/missing")]` on MangaMissingController (Phase 6 Plan 06-09 + F-MISSING-SIGNALR follow-up + canonical-resource-reuse follow-up) | `updatePagedItem<Episode>(queryClient, ['/manga/wanted/missing'], body.resource as Episode)` (mirrors TV `wanted/missing` per-row update shape at lines 359-371; `MangaMissingController` extends `RestControllerWithSignalR<ChapterResource, Chapter>` after canonical-resource-reuse follow-up 2026-05-06 deleted the prior `MissingChapterResource` POCO and broadcasts on `ChapterGrabbedEvent` / `ChapterImportedEvent` / `ChapterFileDeletedEvent`. The `body.resource as Episode` cast is structural — `updatePagedItem` matches by `id` only, and `ChapterResource.Id` (inherited from `RestResource`) carries the chapter id from the broadcast) |
| `manga/wanted/cutoff` | `[V5ApiController("manga/wanted/cutoff")]` on MangaCutoffController (Phase 12 Plan 12-12 + F-CUTOFF-SIGNALR follow-up + canonical-resource-reuse follow-up) | `updatePagedItem<Episode>(queryClient, ['/manga/wanted/cutoff'], body.resource as Episode)` (mirrors TV `wanted/cutoff` per-row update shape; `MangaCutoffController` extends `RestControllerWithSignalR<ChapterResource, Chapter>` after canonical-resource-reuse follow-up 2026-05-06 deleted the prior `MangaCutoffResource` POCO and broadcasts on `ChapterGrabbedEvent` / `ChapterImportedEvent` / `ChapterFileDeletedEvent`. The `body.resource as Episode` cast remains structural — `ChapterResource.Id` carries the chapter id) |

**Lowercase SignalR token vs camelCase URL** (Phase 13 Plan 13-07 Rule 1 fix): the `chapterfile` SignalR resource name is **lowercase** because `RestResource.ResourceName` returns `GetType().Name.ToLowerInvariant().Replace("resource", "")`. The React Query key `['/chapterFile']` is **camelCase** because the HTTP route is auto-derived from the controller class name (`ChapterFileController` → `/api/v5/ChapterFile`). Plan 13-07's `ChapterFileResource_ResourceName_yields_lowercase_chapterfile_signalr_token` fixture pins the lowercase contract; a future regression that adds an explicit `[V5ApiController("chapterFile")]` camelCase override would silently break the JavaScript `===` match. This same dual-casing pattern exists for the pre-existing `chapter` (lowercase token) / `['/chapter']` (camelCase key) handler — it is the canonical SignalR-to-route casing gap.

**Insertion invariant** (Pitfall 1): Every new handler MUST land BEFORE the fall-through `console.error('signalR: Unable to find handler for ${name}')` line — otherwise it's unreachable.

**React Query key namespacing** (Pattern F): Plans 07-04 / 07-05 / 07-06 / 07-09 / 07-10 React Query consumers MUST use the URL-shaped keys (`['/manga']`, `['/manga/queue']`, etc.) verified against the resource names in this dispatch table.

**Cross-reference:** Phase 6 deferred-items.md F-01 (carry-forward) — closed by Phase 7 Plan 07-02 (initial 6-handler dispatch addition); Plan 13-00 Pattern κ (orphan-controller flag) — closed by Phase 13 Plan 13-07 (chapterfile handler addition for the new ChapterFileController SignalR-broadcasting backend controller).

## Cross-References

- [frontend/CLAUDE.md](../../CLAUDE.md) - Frontend overview
- [Series/CLAUDE.md](../Series/CLAUDE.md) - Feature using these components
- [.planning/phases/07-api-v5-frontend-manga-shell/07-02-PLAN.md](../../../.planning/phases/07-api-v5-frontend-manga-shell/07-02-PLAN.md) - SignalR manga handler extension (closes F-01)
