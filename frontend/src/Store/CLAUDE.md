# Store (Redux)

## Purpose

Redux store config + the **factory-driven** Settings/provider/collection action layer. Redux here is the *legacy + global* tier of the hybrid store (Zustand + React Query handle the rest — see root `frontend/CLAUDE.md`).

## Directory Structure

```
Store/
├── createAppStore.js        # Store factory (createStore + middleware)
├── thunks.ts                # Thunk registry
├── scrollPositions.ts
├── Actions/
│   ├── actionTypes.js · baseActions.js · settingsActions.js
│   ├── customFilterActions.js · captchaActions.js · createReducers.js · index.js
│   ├── Settings/            # Per-section slices (customFormats, downloadClients,
│   │                        #   importLists, translationProfiles, delayProfiles, …)
│   └── Creators/            # Action-creator FACTORIES (the core value of this dir):
│                            #   createFetchHandler, createSaveHandler,
│                            #   createServerSideCollectionHandlers,
│                            #   createBulkEditItemHandler, createTestProviderHandler, …
│                            #   + Creators/Reducers/ reducer factories
├── Middleware/              # createPersistState, createSentryMiddleware, middlewares
├── Selectors/               # createClientSideCollectionSelector.js,
│                            #   createSettingsSectionSelector.ts, selectSettings.ts,
│                            #   createSeriesClientSideCollectionItemsSelector.js (Sonarr carry-over)
└── Migrators/               # migrate.js, migrateAddMangaDefaults.js (Phase 17.3 D-12 rename)
```

Per-feature Zustand view-state stores (`Manga/mangaOptionsStore.ts`, `Activity/Queue/queueOptionsStore.ts`, …) live in their feature modules, NOT here.

## Cross-References

- [frontend/CLAUDE.md](../../CLAUDE.md) - Frontend overview
- [Manga/CLAUDE.md](../Manga/CLAUDE.md) - Feature using both Redux and Zustand (Sonarr `Series/` deleted Phase 17.3 Plan 17.3-13)
