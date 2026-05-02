# Frontend

## Purpose

React-based web UI served by the backend at `http://localhost:8989`. Built with TypeScript; uses **three** state stores (Redux + Zustand + TanStack React Query) and SignalR for real-time push.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\`

## Technology Stack

| Technology | Version | Purpose |
|------------|---------|---------|
| React | 18.3.1 | UI framework (concurrent mode) |
| TypeScript | 5.7.2 | Type safety |
| Redux | 4.2.1 | Global state (settings, filters, commands) |
| React Redux | 7.2.4 | Redux bindings |
| Connected React Router | 6.9.3 | Sync router with Redux |
| Zustand | 5.0.3 | Per-feature view state (with localStorage persist) |
| TanStack React Query | 5.61 | API data fetching + cache |
| React Router | 5.2.0 | Client-side routing |
| SignalR | 10.0 | Real-time push from backend |
| FontAwesome | 7.1 | Icons |
| Webpack | 5 | Bundling |
| Babel | 7 | Transpilation |
| Sentry | 7.119 | Error reporting |
| Lodash 4 / Moment 2.30 / Fuse.js 7 | — | Utilities |
| react-window | 1.8 | Virtual scrolling |
| react-dnd | 16.0 | Drag & drop |

## Top-Level Structure

```
frontend/
├── src/                                 # 39 top-level dirs (see "Directory Map" below)
│   ├── App/                             # Root, providers, routing
│   ├── Series/                          # → Manga
│   ├── Episode/                         # → Chapter
│   ├── EpisodeFile/                     # → ChapterFile
│   ├── Season/                          # → Volume
│   ├── Components/                      # Shared UI library (313 files)
│   ├── Store/                           # Redux store
│   ├── Helpers/                         # Custom hooks, utilities
│   ├── Settings/                        # Settings pages (273 files)
│   ├── Activity/                        # Queue / History / Blocklist
│   ├── Calendar/                        # Calendar view
│   ├── Wanted/                          # Missing / CutoffUnmet
│   ├── AddSeries/                       # Add-series flow → AddManga
│   ├── InteractiveSearch/               # Manual release search
│   ├── InteractiveImport/               # Manual file import
│   ├── Organize/                        # File organize preview
│   ├── Parse/                           # Title-parse utility
│   ├── System/                          # Status / Tasks / Logs / Backup / Updates / Events
│   ├── Commands/                        # Command execution
│   ├── Utilities/                       # Pure utility functions (50+ files)
│   ├── typings/                         # TypeScript type definitions
│   ├── Quality/, Language/, Tags/, Filters/, RootFolder/, Path/, DownloadClient/, OAuth/, FirstRun/, Internationalization/, Diag/, Shared/   # Smaller modules
│   ├── Styles/                          # Global CSS / themes / variables
│   ├── Content/                         # Static assets (fonts, icons, manifest)
│   ├── index.ts                         # Webpack entry point
│   └── bootstrap.tsx                    # React initialization
├── build/
│   ├── webpack.config.js                # Webpack config
│   └── webpack/                         # Custom loaders
├── .eslintrc.js
├── .prettierrc.json
├── .stylelintrc
└── tsconfig.json
```

## Application Bootstrap

```
index.ts
  ├─ Fetch /initialize.json → set window.Sonarr (apiKey, urlBase, version, instanceName, branch)
  ├─ Set webpack publicPath from urlBase
  └─ import('./bootstrap')

bootstrap.tsx
  ├─ Create browser history (with urlBase basename)
  ├─ Create Redux store: createAppStore(history)
  ├─ Create React 18 root
  └─ Render <App store history />

App.tsx (provider chain, top → bottom)
  <DocumentTitle title={instanceName}>
    <QueryClientProvider client={queryClient}>            ← React Query
      <Provider store={store}>                             ← Redux
        <ConnectedRouter history={history}>               ← Router
          <ApplyTheme>                                     ← Theme apply
            <Page>                                         ← Sidebar+content layout
              <AppRoutes />                                ← Route switch
```

## Routing (frontend/src/App/AppRoutes.tsx)

| Path | Component | Page |
|------|-----------|------|
| `/` | `SeriesIndex` | Series list (home) |
| `/add/new` | `AddNewSeries` | Add new |
| `/add/import` | `ImportSeriesPage` | Import existing folder |
| `/series/:titleSlug` | `SeriesDetailsPage` | Series detail |
| `/calendar` | `CalendarPage` | Calendar |
| `/activity/queue` | `Queue` | Active downloads |
| `/activity/history` | `History` | Grab/import history |
| `/activity/blocklist` | `Blocklist` | Blocked releases |
| `/wanted/missing` | `Missing` | Missing episodes |
| `/wanted/cutoffunmet` | `CutoffUnmet` | Below cutoff |
| `/settings` | `Settings` | Settings home |
| `/settings/mediamanagement` | `MediaManagement` | File handling |
| `/settings/profiles` | `Profiles` | Quality / language / delay / release |
| `/settings/quality` | `Quality` | Quality definitions |
| `/settings/customformats` | `CustomFormatSettingsPage` | Custom format rules |
| `/settings/indexers` | `IndexerSettings` | Indexers + options |
| `/settings/downloadclients` | `DownloadClientSettings` | Clients + remote path mappings |
| `/settings/importlists` | `ImportListSettings` | Lists + exclusions |
| `/settings/connect` | `NotificationSettings` | Notifications |
| `/settings/metadata` | `MetadataSettings` | Metadata writers |
| `/settings/metadatasource` | `MetadataSourceSettings` | TVDB → manga sources |
| `/settings/tags` | `TagSettings` | Tags + auto-tagging |
| `/settings/general` | `GeneralSettings` | Host / port / proxy / auth / SSL / logging |
| `/settings/ui` | `UISettings` | Theme / language / time format |
| `/system/status` | `Status` | System info |
| `/system/tasks` | `Tasks` | Queued + scheduled |
| `/system/backup` | `Backups` | Backups |
| `/system/updates` | `Updates` | Update history |
| `/system/events` | `LogsTable` | Log events |
| `/system/logs/files` | `Logs` | Raw log files |
| `*` | `NotFound` | Catch-all |

## State Management (Hybrid)

### 1. React Query (Server State — Preferred for new work)
```typescript
import { useApiQuery } from 'Helpers/Hooks/useApiQuery';

const { data, isLoading, isFetched, error } = useApiQuery<Series[]>({
  queryKey: ['/series'],
  staleTime: 5 * 60 * 1000,
});
```

```typescript
import { useApiMutation } from 'Helpers/Hooks/useApiMutation';

const { mutate, isPending } = useApiMutation<Series>({
  method: 'PUT',
  queryKey: ['/series', id],
});
mutate({ ...series, monitored: true });
```

### 2. Zustand (UI Preferences — Persisted)
```typescript
const useSeriesOptions = create(persist((set) => ({
  view: 'posters',
  setView: (view) => set({ view }),
}), { name: 'series_options' }));
```

### 3. Redux (Settings, Filters, Commands — Legacy + global)
```typescript
const settings = useSelector((state) => state.settings);
dispatch(setSettingValue('ui', 'theme', 'dark'));
```

`Store/Actions/Creators/` provides factories like `createFetchHandler`, `createSaveHandler`, `createBulkEditItemHandler`, `createServerSideCollectionHandlers` for the heavy settings/queue/history flows that retain Redux.

## SignalR Real-Time Updates

A `<SignalRListener />` component (or Redux middleware) opens `/signalr/messages?access_token=<apiKey>` and dispatches incoming messages as Redux actions / React Query cache invalidations.

| Backend Message | Frontend Reaction |
|-----------------|-------------------|
| `series` | Invalidate `[/series]` queries; update single series cache |
| `episode` | Update episode cache for that series |
| `episodefile` | Update file list |
| `command` | Update command queue / progress UI |
| `queue` | Update active queue list |
| `history` | Append to history list |
| `health` | Update health badge |
| `system` | Show notice / restart prompt |

## Component Conventions

### Feature Module Layout
```
FeatureName/
├── FeatureName.ts            # Type definition
├── useFeatureName.ts         # API hooks (CRUD)
├── featureNameOptionsStore.ts# Zustand persisted UI state
├── Index/                    # List view (often w/ multiple display modes)
├── Details/                  # Detail view
├── Edit/                     # Edit modal
├── Delete/                   # Delete modal
└── Search/, History/, …      # Sub-features
```

### CSS Modules
```typescript
import styles from './SeriesIndex.module.css';
<div className={styles.container}>…</div>
```

Global CSS / variables / themes live in `frontend/src/Styles/`.

### TypeScript Path Aliases
The `tsconfig.json` and Webpack alias `frontend/src/` as the import root. Imports like `import { useApiQuery } from 'Helpers/Hooks/useApiQuery'` resolve to `frontend/src/Helpers/Hooks/useApiQuery.ts`.

## Build Commands

```bash
yarn build                  # Dev build
yarn build --env production # Prod (minified, source maps)
yarn watch                  # Webpack watch
yarn clean                  # Clean _output/UI/
yarn lint && yarn lint-fix  # ESLint
yarn stylelint              # CSS lint
```

## Output Directory

`yarn build` writes to **`../_output/UI/`** (consumed by `Sonarr.Http.Frontend.Mappers` to serve the SPA). Asset paths use `window.Sonarr.urlBase` for reverse-proxy compatibility.

## Manga Adaptation: High-Level Plan

Listed by approximate priority. Detailed migration notes are in each module's `CLAUDE.md`.

| Module | Effort | Action |
|--------|--------|--------|
| `Series/` → `Manga/` | HIGH | Rename folder + types; remove tvdbId/seasonFolder/airTime; add mangadexId/anilistId/author/artist |
| `Episode/` → `Chapter/` | HIGH | Remove airDate/sceneEpisodeNumber; add releaseDate/pageCount/scanlationGroup |
| `Season/` → `Volume/` | MED | Optional grouping for manga |
| `EpisodeFile/` → `ChapterFile/` | MED | Rename, update mediaInfo |
| `AddSeries/` → `AddManga/` | HIGH | Rebrand entire flow |
| `Calendar/` | MED | Manga release schedules differ from TV airing |
| `Activity/` | LOW | Update terminology |
| `Wanted/` | LOW | Logic transfers; rename references |
| `InteractiveSearch/Import/` | MED | Update match flow |
| `Settings/Quality, MetadataSource, Indexers` | HIGH | Quality tiers, manga metadata sources, manga indexers |
| `Components/`, `Helpers/`, `Utilities/`, `System/`, `Diag/`, `Internationalization/`, `Tags/`, `OAuth/`, `FirstRun/` | NONE | Generic utilities — keep |
| `Styles/`, `Content/` | LOW | App branding (icons, manifest, name) |
| `typings/` | MED | Series/Episode types renamed |
| `Store/Actions/Settings/*` | LOW | Some entity slices reference series/episode terminology |

## Documentation Index

| Path | Purpose |
|------|---------|
| [src/App/CLAUDE.md](./src/App/CLAUDE.md) | Root + routing + providers |
| [src/Series/CLAUDE.md](./src/Series/CLAUDE.md) | Series feature → Manga |
| [src/Episode/CLAUDE.md](./src/Episode/CLAUDE.md) | Episode feature → Chapter |
| [src/Components/CLAUDE.md](./src/Components/CLAUDE.md) | Shared UI library |
| [src/Store/CLAUDE.md](./src/Store/CLAUDE.md) | Redux store |
| [src/Helpers/CLAUDE.md](./src/Helpers/CLAUDE.md) | Custom hooks |
| [src/Settings/CLAUDE.md](./src/Settings/CLAUDE.md) | Settings pages |
| [src/AddSeries/CLAUDE.md](./src/AddSeries/CLAUDE.md) | Add-series flow |
| [src/Activity/CLAUDE.md](./src/Activity/CLAUDE.md) | Queue/History/Blocklist |
| [src/Calendar/CLAUDE.md](./src/Calendar/CLAUDE.md) | Calendar |
| [src/Wanted/CLAUDE.md](./src/Wanted/CLAUDE.md) | Missing / CutoffUnmet |
| [src/InteractiveSearch/CLAUDE.md](./src/InteractiveSearch/CLAUDE.md) | Manual release search |
| [src/InteractiveImport/CLAUDE.md](./src/InteractiveImport/CLAUDE.md) | Manual file import |
| [src/System/CLAUDE.md](./src/System/CLAUDE.md) | System admin pages |
| [src/Utilities/CLAUDE.md](./src/Utilities/CLAUDE.md) | Pure helpers |
| [src/typings/CLAUDE.md](./src/typings/CLAUDE.md) | API DTO types |

## Cross-References

- [PROJECT_CONTEXT.md](../PROJECT_CONTEXT.md) — Overall architecture
- [src/Sonarr.Api.V5/CLAUDE.md](../src/Sonarr.Api.V5/CLAUDE.md) — Backend API consumed
- [src/NzbDrone.SignalR/CLAUDE.md](../src/NzbDrone.SignalR/CLAUDE.md) — Real-time push
