# App/

## Purpose

Application root — providers (Redux, React Query, Router), routing configuration, theme, app-level state, page layout shell.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\src\App\`

## Files

| File | Purpose |
|------|---------|
| `App.tsx` | Root React component. Wraps the entire UI with provider chain. |
| `AppRoutes.tsx` | All route definitions (single-source-of-truth for paths). |
| `appStore.ts` | (or older `Store/createAppStore.js`) Redux store factory wired here |
| `messagesStore.ts` | Zustand store for transient app-level toast messages |
| `ApplyTheme.tsx` | Reads UI theme setting and applies CSS class to `<html>` |
| `ColorImpairedContext.ts` | React context for color-blind-friendly mode |

## Subdirectories

| Folder | Purpose |
|--------|---------|
| `Select/` | Selection-state hooks for "select multiple rows" features |
| `State/` | Redux state slices specific to App-level concerns |

## Provider Tree (App.tsx)

```tsx
<DocumentTitle title={window.Mangarr.instanceName}>
  <QueryClientProvider client={queryClient}>            // TanStack React Query
    <Provider store={store}>                             // Redux
      <ConnectedRouter history={history}>               // Connected React Router
        <ApplyTheme>                                     // Theme application
          <Page>                                         // Sidebar + content layout
            <AppRoutes />
          </Page>
        </ApplyTheme>
      </ConnectedRouter>
    </Provider>
  </QueryClientProvider>
</DocumentTitle>
```

## Routing Table (AppRoutes.tsx)

| Path | Component | Notes |
|------|-----------|-------|
| `/` | `SeriesIndex` | Home page (will become MangaIndex) |
| `/add/new` | `AddNewSeries` | Search & add (→ AddNewManga) |
| `/add/import` | `ImportSeriesPage` | Import existing folder (→ ImportMangaPage) |
| `/series/:titleSlug` | `SeriesDetailsPage` | Detail (→ MangaDetailsPage) |
| `/calendar` | `CalendarPage` | Upcoming releases |
| `/activity/queue` | `Queue` | Active downloads |
| `/activity/history` | `History` | Grab/import history |
| `/activity/blocklist` | `Blocklist` | Failed releases |
| `/wanted/missing` | `Missing` | Missing chapters |
| `/wanted/cutoffunmet` | `CutoffUnmet` | Below cutoff |
| `/settings` | `Settings` | Settings home |
| `/settings/mediamanagement` | `MediaManagement` | File management |
| `/settings/profiles` | `Profiles` | Quality / language / delay / release profiles |
| `/settings/quality` | `Quality` | Quality definitions |
| `/settings/customformats` | `CustomFormatSettingsPage` | Custom format rules |
| `/settings/indexers` | `IndexerSettings` | Indexer config |
| `/settings/downloadclients` | `DownloadClientSettings` | Client config |
| `/settings/importlists` | `ImportListSettings` | Lists |
| `/settings/connect` | `NotificationSettings` | Notification providers |
| `/settings/metadata` | `MetadataSettings` | Metadata writers |
| `/settings/metadatasource` | `MetadataSourceSettings` | TVDB → manga sources |
| `/settings/tags` | `TagSettings` | Tags + auto-tagging |
| `/settings/general` | `GeneralSettings` | Host / port / proxy / auth / SSL / logging |
| `/settings/ui` | `UISettings` | Theme / language / time format |
| `/system/status` | `Status` | System info |
| `/system/tasks` | `Tasks` | Scheduled + queued |
| `/system/backup` | `Backups` | Backup management |
| `/system/updates` | `Updates` | Update history |
| `/system/events` | `LogsTable` | Log events viewer |
| `/system/logs/files` | `Logs` | Raw log files |
| `*` | `NotFound` | 404 |

## Adding a New Route

1. Edit `AppRoutes.tsx`.
2. Import the new page component.
3. Add a `<Route path="..." component={NewPage} />` entry.
4. Add a sidebar link in `Components/Page/Sidebar/`.
5. If it's a new top-level concept, create a folder under `frontend/src/` with its own `CLAUDE.md`.

## Window Globals

`window.Mangarr` is set on bootstrap from `/initialize.json`:

```typescript
interface SonarrWindow {
  apiKey: string;
  apiRoot: string;          // e.g., "/api/v5"
  signalRoot: string;       // SignalR hub URL
  urlBase: string;          // For reverse proxies (e.g. "/sonarr")
  version: string;
  branch: string;
  analytics: boolean;
  instanceName: string;     // Used as page title
  theme: string;
  isProduction: boolean;
}
```

Used throughout the codebase via `window.Mangarr` (no helper hook).

## Manga Adaptation Notes

| File | Action |
|------|--------|
| `App.tsx` | `window.Mangarr` should eventually become `window.Mangarr`. Keep both during transition. |
| `AppRoutes.tsx` | Add `/manga/...` routes alongside `/series/...` during transition. Eventually drop the latter. |
| Page titles | Reference "Mangarr" via `instanceName` — update server-side to "Mangarr" by default |

## Cross-References

- [../../CLAUDE.md](../../CLAUDE.md) — Frontend overview
- [../Series/CLAUDE.md](../Series/CLAUDE.md) — Home page
- [../Components/CLAUDE.md](../Components/CLAUDE.md) — `<Page>` layout
- [../Store/CLAUDE.md](../Store/CLAUDE.md) — Redux store
