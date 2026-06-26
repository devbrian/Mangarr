# App/

## Purpose

Application root — providers (Redux, React Query, Router), routing configuration, theme, app-level state, page layout shell.

## Files

| File | Purpose |
|------|---------|
| `App.tsx` | Root React component. Wraps the entire UI with the provider chain. |
| `AppRoutes.tsx` | All route definitions (single source of truth for paths). |
| `appStore.ts` | Redux store factory (`createAppStore(history)`) |
| `queryClient.ts` | TanStack React Query client singleton |
| `messagesStore.ts` | Zustand store for transient app-level toast messages |
| `ApplyTheme.tsx` | Reads UI theme setting and applies CSS class to `<html>` |
| `ColorImpairedContext.ts` | React context for color-blind-friendly mode |
| `ModelBase.ts` | Base shape (`id`) for API resources |
| `useTranslations.ts` | i18n string-table hook |
| `AppUpdatedModal*.tsx` / `ConnectionLostModal.tsx` | App-update + connection-lost modals |

## Subdirectories

| Folder | Purpose |
|--------|---------|
| `Select/` | Selection-state hooks for "select multiple rows" features |
| `State/` | Redux state slices specific to App-level concerns |

## Provider Tree (App.tsx)

```tsx
<DocumentTitle title={window.Mangarr.instanceName || 'Mangarr'}>
  <QueryClientProvider client={queryClient}>            // TanStack React Query
    <Provider store={store}>                             // Redux
      <ConnectedRouter history={history}>               // Connected React Router
        <ApplyTheme />                                   // Theme application (self-closing sibling)
        <Page>                                           // Sidebar + content layout
          <AppRoutes />
        </Page>
      </ConnectedRouter>
    </Provider>
  </QueryClientProvider>
</DocumentTitle>
```

## Routing Table (AppRoutes.tsx)

Manga-canonical routes (Sonarr `Series/Episode` routes were cut over in Phase 7+; stub dirs deleted Phase 17.3).

| Path | Component | Notes |
|------|-----------|-------|
| `/` | `MangaIndex` | Home / manga library |
| `/manga/:titleSlug` | `MangaDetailsPage` | Manga detail |
| `/add/manga` | `AddNewManga` | Search & add |
| `/add/import` | `ImportMangaPage` | Import existing folder |
| `/discovery` | `Discovery` | Filtered bulk-add browse (Phase 42, NEW-in-Mangarr) |
| `/calendar` | `CalendarPage` | Upcoming releases |
| `/manga/activity/queue` | `MangaQueue` | Active downloads |
| `/manga/activity/history` | `MangaHistory` | Grab/import history |
| `/manga/activity/blocklist` | `MangaBlocklist` | Failed releases |
| `/manga/wanted/missing` | `MangaMissing` | Missing chapters |
| `/manga/wanted/cutoffunmet` | `MangaCutoffUnmet` | Below cutoff |
| `/settings` | `Settings` | Settings home |
| `/settings/mediamanagement` | `MediaManagement` | File management |
| `/settings/profiles` | `Profiles` | Translation / delay / release profiles |
| `/settings/customformatprofiles` | `CustomFormatProfileSettings` | Custom format profiles |
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

1. Edit `AppRoutes.tsx`, import the page, add `<Route path="..." component={NewPage} />`.
2. Add a sidebar link in `Components/Page/Sidebar/`.
3. New top-level concept → create a folder under `frontend/src/` with its own `CLAUDE.md`.

## Window Globals

`window.Mangarr` is set on bootstrap from `/initialize.json` — `apiKey`, `apiRoot` (e.g. `/api/v5`), `urlBase` (reverse-proxy base), `version`, `branch`, `instanceName` (page title), `theme`, `isProduction`. Used directly via `window.Mangarr` (no helper hook). (Renamed from `window.Sonarr` in Phase 15 Plan 15-08.)

## Cross-References

- [../../CLAUDE.md](../../CLAUDE.md) — Frontend overview
- [../Manga/CLAUDE.md](../Manga/CLAUDE.md) — Home page (Sonarr `Series/` deleted Phase 17.3 Plan 17.3-13)
- [../Components/CLAUDE.md](../Components/CLAUDE.md) — `<Page>` layout
- [../Store/CLAUDE.md](../Store/CLAUDE.md) — Redux store
