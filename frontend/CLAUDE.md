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
├── src/                                 # Top-level dirs (post-Phase-17.3 manga-canonical tree)
│   ├── App/                             # Root, providers, routing
│   ├── Manga/                           # Manga library (canonical; Sonarr Series/ stub-dir deleted in Plan 17.3-13)
│   ├── Chapter/                         # Chapter cells/hooks (canonical; Sonarr Episode/ stub-dir deleted in Plan 17.3-13)
│   ├── ChapterFile/                     # ChapterFile row / delete-modal / hook
│   ├── AddManga/                        # Add-manga flow (canonical; Sonarr AddSeries/ stub-dir deleted in Plan 17.3-13)
│   ├── Discovery/                       # Discover / browse popular titles (/discovery)
│   ├── Components/                      # Shared UI library (313 files)
│   ├── Store/                           # Redux store
│   ├── Helpers/                         # Custom hooks, utilities
│   ├── Settings/                        # Settings pages (273 files)
│   ├── Activity/                        # Queue / History / Blocklist
│   ├── Wanted/                          # Missing / CutoffUnmet
│   ├── InteractiveSearch/               # Manual release search
│   ├── InteractiveImport/               # Manual file import
│   ├── Calendar/                        # Release calendar (/calendar)
│   ├── Organize/                        # File organize preview
│   ├── Parse/                           # Title-parse utility
│   ├── System/                          # Status / Tasks / Logs / Backup / Updates / Events
│   ├── Commands/                        # Command execution
│   ├── Utilities/                       # Pure utility functions (Utilities/Series/ + Utilities/Episode/ deleted in Plan 17.3-13)
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

Note: Sonarr's `Season/` + `EpisodeFile/` (frontend) had no manga-canonical peer surface that survived Phase 17.3 — `Season/` per PROJECT.md Volumes/Seasons Out-of-Scope (`volumeNumber` is display-only on `Chapter`), `EpisodeFile/` per Phase 15 cutover (the `ChapterFile` wire shape is consumed directly by `Manga/Details/` + `Activity/` without a dedicated frontend feature dir). Both were Phase 15 Plan 15-12 stubs and were deleted with the rest in Plan 17.3-13.

## Application Bootstrap

```
index.ts
  ├─ Fetch /initialize.json → set window.Mangarr (apiKey, urlBase, version, instanceName, branch)
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
| `/` | `MangaIndex` | Manga list (home) |
| `/add/manga` | `AddNewManga` | Add new |
| `/add/import` | `ImportMangaPage` | Import existing folder |
| `/discovery` | `Discovery` | Discover / browse popular titles |
| `/manga/:titleSlug` | (manga details) | Manga detail |
| `/calendar` | `CalendarPage` | Calendar |
| `/manga/activity/queue` | `MangaQueue` | Active downloads |
| `/manga/activity/history` | `MangaHistory` | Grab/import history |
| `/manga/activity/blocklist` | `MangaBlocklist` | Blocked releases |
| `/manga/wanted/missing` | `MangaMissing` | Missing chapters |
| `/manga/wanted/cutoffunmet` | `MangaCutoffUnmet` | Below cutoff |
| `/settings` | `Settings` | Settings home |
| `/settings/mediamanagement` | `MediaManagement` | File handling |
| `/settings/profiles` | `Profiles` | Translation profiles |
| `/settings/customformatprofiles` | (custom format profiles) | Custom format profiles |
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

const { data, isLoading, isFetched, error } = useApiQuery<Manga[]>({
  queryKey: ['/manga'],
  staleTime: 5 * 60 * 1000,
});
```

```typescript
import { useApiMutation } from 'Helpers/Hooks/useApiMutation';

const { mutate, isPending } = useApiMutation<Manga>({
  method: 'PUT',
  queryKey: ['/manga', id],
});
mutate({ ...manga, monitored: true });
```

### 2. Zustand (UI Preferences — Persisted)
```typescript
const useMangaOptions = create(persist((set) => ({
  view: 'posters',
  setView: (view) => set({ view }),
}), { name: 'manga_options' }));
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
| `manga` | Invalidate `['/manga']` queries; update single manga cache |
| `chapter` | Update chapter cache for that manga |
| `chapterfile` | Update file list |
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
import styles from './MangaIndex.css';
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

`yarn build` writes to **`../_output/UI/`** (consumed by `Mangarr.Http.Frontend.Mappers` to serve the SPA). Asset paths use `window.Mangarr.urlBase` for reverse-proxy compatibility.

## Manga Adaptation: High-Level Plan

Listed by approximate priority. Detailed migration notes are in each module's `CLAUDE.md`.

| Module | Effort | Status |
|--------|--------|--------|
| `Series/` → `Manga/` | HIGH | **Done** — Phase 7 (Manga/ shipped); Phase 15 Plan 15-12 (Series/ became thin re-export stubs); Phase 17.3 Plan 17.3-13 (atomic stub-dir delete) |
| `Episode/` → `Chapter/` | HIGH | **Done** — Phase 7 (Chapter/ shipped); Plan 15-12 stubs + Plan 17.3-13 delete |
| `Season/` (no Manga peer) | — | **Done — no-op** — `Season/` deleted in Plan 17.3-13; `volumeNumber` is display-only on Chapter per PROJECT.md Out-of-Scope |
| `EpisodeFile/` (no dedicated frontend peer) | — | **Done — stub-only** — `EpisodeFile/` deleted in Plan 17.3-13; `ChapterFile` wire shape consumed directly by `Manga/Details/` + `Activity/` |
| `AddSeries/` → `AddManga/` | HIGH | **Done** — Phase 7 Plan 07-06 (AddManga/ shipped); Plan 17.3-13 stub-dir delete |
| `Activity/` | LOW | **Done** — Phase 7 Plan 07-09 thin wrappers (`MangaQueue` / `MangaHistory` / `MangaBlocklist`); GH #73 row-component migration (2026-05-11); EpisodeCellContent stubs deleted in Plan 17.3-04 |
| `Wanted/` | LOW | **Done** — Phase 6/12 (`MissingChaptersController` + `MangaCutoffController`) |
| `InteractiveSearch/Import/` | MED | **Done** — Phase 11+ |
| `Settings/Quality, MetadataSource, Indexers` | HIGH | **Done** — Phase 5 D-04 dropped TV quality; TranslationProfile + CustomFormatProfile shipped; MangaDex MetadataSource + MangaDex/Comix indexers shipped |
| `Components/`, `Helpers/`, `Utilities/`, `System/`, `Diag/`, `Internationalization/`, `Tags/`, `OAuth/`, `FirstRun/` | NONE | Generic utilities — kept verbatim (8 Components/Form/* renames landed in Plan 17.3-04 D-07 for vocabulary parity) |
| `Styles/`, `Content/` | LOW | **Done** — Phase 15 rebrand (icons / manifest / favicon flipped) |
| `typings/` | MED | **Done** — Plan 17.3-13 retired stub-dir types (`Series.ts` / `Episode.ts` / `EpisodeFile.ts`); cross-cutting types in `typings/` already manga-shape |
| `Store/Actions/Settings/*` | LOW | **In-progress** — entity slices reference series/episode terminology; Phase 17.3 Plan 17.3-12 D-12 renamed `migrateAddSeriesDefaults.js` → `migrateAddMangaDefaults.js` |

## Documentation Index

| Path | Purpose |
|------|---------|
| [src/App/CLAUDE.md](./src/App/CLAUDE.md) | Root + routing + providers |
| [src/Manga/CLAUDE.md](./src/Manga/CLAUDE.md) | Manga library + Index/Details (canonical; Series/ stub deleted Plan 17.3-13) |
| [src/Chapter/CLAUDE.md](./src/Chapter/CLAUDE.md) | Chapter cells / hooks / status (canonical; Episode/ stub deleted Plan 17.3-13) |
| [src/ChapterFile/CLAUDE.md](./src/ChapterFile/CLAUDE.md) | ChapterFile row / delete-modal / hook |
| [src/AddManga/CLAUDE.md](./src/AddManga/CLAUDE.md) | Add-manga flow (canonical; AddSeries/ stub deleted Plan 17.3-13) |
| [src/Discovery/CLAUDE.md](./src/Discovery/CLAUDE.md) | Discover / browse popular titles (/discovery) |
| [src/Components/CLAUDE.md](./src/Components/CLAUDE.md) | Shared UI library |
| [src/Store/CLAUDE.md](./src/Store/CLAUDE.md) | Redux store |
| [src/Helpers/CLAUDE.md](./src/Helpers/CLAUDE.md) | Custom hooks |
| [src/Settings/CLAUDE.md](./src/Settings/CLAUDE.md) | Settings pages |
| [src/Activity/CLAUDE.md](./src/Activity/CLAUDE.md) | Queue/History/Blocklist |
| [src/Wanted/CLAUDE.md](./src/Wanted/CLAUDE.md) | Missing / CutoffUnmet |
| [src/InteractiveSearch/CLAUDE.md](./src/InteractiveSearch/CLAUDE.md) | Manual release search |
| [src/InteractiveImport/CLAUDE.md](./src/InteractiveImport/CLAUDE.md) | Manual file import |
| [src/System/CLAUDE.md](./src/System/CLAUDE.md) | System admin pages |
| [src/Utilities/CLAUDE.md](./src/Utilities/CLAUDE.md) | Pure helpers |
| [src/typings/CLAUDE.md](./src/typings/CLAUDE.md) | API DTO types |

## Cross-References

- [PROJECT_CONTEXT.md](../PROJECT_CONTEXT.md) — Overall architecture
- [src/Mangarr.Api.V5/CLAUDE.md](../src/Mangarr.Api.V5/CLAUDE.md) — Backend API consumed
- [src/NzbDrone.SignalR/CLAUDE.md](../src/NzbDrone.SignalR/CLAUDE.md) — Real-time push
