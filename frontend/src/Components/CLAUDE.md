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

Hub-message dispatch table that opens `/signalr/messages?access_token=<apiKey>` on mount and routes per-resource pushes to React Query cache invalidations / Redux store updates / `repopulatePage` calls. Phase 7 Plan 07-02 extends the dispatch table with **6 manga resource handlers** (closes Phase 6 deferred-items.md F-01); Phase 12 follow-up (F-CUTOFF-SIGNALR closure, 2026-05-06) adds a **7th** handler for `manga/wanted/cutoff`:

| Resource | Backend `[V5ApiController(...)]` route | Handler shape |
|----------|----------------------------------------|---------------|
| `manga` | `[V5ApiController]` (bare) on MangaController | `updateQueryClientItem` / `removeQueryClientItem` against `['/manga']` (mirrors `series` handler) |
| `chapter` | `[V5ApiController]` (bare) on ChapterController (Plan 07-01) | `updateQueryClientItem` against `['/chapter']` (mirrors `episode` handler) |
| `manga/queue` | `[V5ApiController("manga/queue")]` on MangaQueueController | `queryClient.invalidateQueries({ queryKey: ['/manga/queue'] })` |
| `manga/blocklist` | `[V5ApiController("manga/blocklist")]` on MangaBlocklistController | `queryClient.invalidateQueries({ queryKey: ['/manga/blocklist'] })` |
| `manga/history` | `[V5ApiController("manga/history")]` on ChapterHistoryController | `queryClient.invalidateQueries({ queryKey: ['/manga/history'] })` |
| `manga/wanted/missing` | `[V5ApiController("manga/wanted/missing")]` on MissingChaptersController | `queryClient.invalidateQueries({ queryKey: ['/manga/wanted/missing'] })` |
| `manga/wanted/cutoff` | `[V5ApiController("manga/wanted/cutoff")]` on MangaCutoffController (Phase 12 Plan 12-12 + F-CUTOFF-SIGNALR follow-up) | `updatePagedItem<Episode>(queryClient, ['/manga/wanted/cutoff'], body.resource as Episode)` (mirrors TV `wanted/cutoff` per-row update shape; `MangaCutoffController` extends `RestControllerWithSignalR<MangaCutoffResource, Chapter>` and broadcasts on `ChapterGrabbedEvent` / `ChapterImportedEvent` / `ChapterFileDeletedEvent`) |

**Insertion invariant** (Pitfall 1): Every new handler MUST land BEFORE the fall-through `console.error('signalR: Unable to find handler for ${name}')` line — otherwise it's unreachable.

**React Query key namespacing** (Pattern F): Plans 07-04 / 07-05 / 07-06 / 07-09 / 07-10 React Query consumers MUST use the URL-shaped keys (`['/manga']`, `['/manga/queue']`, etc.) verified against the resource names in this dispatch table.

**Cross-reference:** Phase 6 deferred-items.md F-01 (carry-forward) — closed by Phase 7 Plan 07-02 (this dispatch addition).

## Cross-References

- [frontend/CLAUDE.md](../../CLAUDE.md) - Frontend overview
- [Series/CLAUDE.md](../Series/CLAUDE.md) - Feature using these components
- [.planning/phases/07-api-v5-frontend-manga-shell/07-02-PLAN.md](../../../.planning/phases/07-api-v5-frontend-manga-shell/07-02-PLAN.md) - SignalR manga handler extension (closes F-01)
