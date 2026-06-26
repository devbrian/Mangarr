# AddNewManga/

## Purpose

The **Add Manga** flow page subtree — search a metadata source for a manga
title, configure its monitoring/profile/tag form, save. Parallel sibling of
`frontend/src/AddSeries/AddNewSeries/`. Mirrors AddSeries' search-grid +
side-panel-modal shape verbatim per Phase 7 D-04; only the form-field set
diverges (5-value `MangaMonitor`, TranslationProfile, CustomFormatProfile,
SearchOnAdd toggle).


## Key Files

| File | Purpose |
|------|---------|
| `AddNewManga.tsx` | Page component — wraps `PageContent` shell, debounced (500ms) lookup, render results grid OR empty/error state. |
| `AddNewMangaSearchResult.tsx` | One result card — cover thumbnail + title + status pill + author + chapter count + external link to MangaDex/AniList/MAL. Click-through opens `AddNewMangaModal`. |
| `AddNewMangaModal.tsx` | Thin `Modal` wrapper that renders `AddNewMangaModalContent`. |
| `AddNewMangaModalContent.tsx` | Side-panel modal content: result header + Root Folder picker, then delegates the `<Form>` field body to `AddMangaFormBody`. |
| `AddMangaFormBody.tsx` | Factored Add-Manga `<Form>` body (Phase 42 Plan 42-07) — Root Folder (`/api/v5/rootfolder`), Monitor (5-value `MangaMonitor`), Translation Profile (`/api/v5/translationprofile`), Custom Format Profile (`/api/v5/customformatprofile`), Tags, SearchOnAdd toggle. Shared by the single-add modal here and the bulk-add `Discovery/AddTopX` modal. |
| `useAddManga.ts` | `useLookupManga(query)` (GET /api/v5/manga/lookup?term=) + `useAddManga()` (POST /api/v5/manga). Both honor the React Query key contract from Plan 07-02 (`['/manga']`). |
| `*.css` / `*.css.d.ts` | Verbatim CSS Module copies of the AddSeries CSS files; class names are scoped automatically. |

## Add-Manga Flow

```
User types title (debounced 500ms)
  -> useLookupManga -> GET /api/v5/manga/lookup?term=
  -> AddNewMangaSearchResult cards render in 3-column grid
User clicks a result
  -> AddNewMangaModal opens with the side-panel form
User picks Root Folder, Monitor, TranslationProfile, CustomFormatProfile,
       Tags, optionally toggles "Start search for missing chapters"
  -> User clicks "Add Manga {Title}"
  -> useAddManga -> POST /api/v5/manga { ...AddMangaPayload }
  -> Backend MangaController.AddManga persists + queues refresh
  -> SignalR pushes `manga` (action=added) -> SignalRListener invalidates ['/manga']
  -> useAddManga.onSuccess writes the new manga into the ['/manga'] queryCache
  -> Modal auto-closes; user stays on /add/manga (the result row gets a
     green "already in library" checkmark — Sonarr-mirror UX verified
     against pre-Phase-15-delete useAddSeries.ts, issue #102 close-out).
     To reach detail page, user clicks the sidebar /manga link or the
     in-place result-row indicator.
```

## Form-Field Divergence (Mangarr -> Mangarr)

Per Phase 7 D-04 + Phase 6 D-03 + Phase 6 D-06.

| Mangarr field | Manga field | Source endpoint | Notes |
|--------------|-------------|------------------|-------|
| `rootFolderPath` | `rootFolderPath` | `/api/v5/rootfolder` | unchanged |
| `monitor` (11-value SeriesMonitor) | `monitor` (5-value MangaMonitor) | inline values array | Phase 6 D-03 |
| `qualityProfileId` | `translationProfileId` | `/api/v5/translationprofile` | Phase 5 D-01 |
| (none) | `customFormatProfileId` | `/api/v5/customformatprofile` | Phase 5 D-07 |
| `seriesType` | dropped | n/a | manga has no anime/standard/daily |
| `seasonFolder` | dropped | n/a | Volumes/Seasons OoS |
| `searchForMissingEpisodes` | `searchForMissingChapters` | (form) | Phase 6 D-06 SearchOnAdd |
| `searchForCutoffUnmetEpisodes` | dropped | n/a | manga has no cutoff yet |
| `tags` | `tags` | `/api/v5/tag` | unchanged |

## Patterns / Conventions

- **Debounce 500ms** on the search input (Mangarr ships 300ms; manga uses 500ms
  because MangaDex lookups are slower than TVDB — UI-SPEC §AddManga Layout
  Contract).
- **Cover URL** flows through `manga.images[].url` (already rewritten to
  `/MediaCover/manga/{id}/poster.jpg` by `MangaController.MapResource` per
  Phase 2). The Lookup endpoint returns the same shape.
- **Profile fall-back sentinel** — `translationProfileId: 0` /
  `customFormatProfileId: 0` mean "fall back to Config defaults" per Phase 5
  D-11 default-seeded entities. The form re-binds the value to the first real
  profile id once the corresponding `useApiQuery` resolves.
- **No HeartRating** — UI-SPEC §AddManga visual hierarchy lists cover, title,
  status pill, year+chapter count as the result-card anchors; ratings are NOT
  primary anchors for v1.
- **Single-add only** — Lock #12. The `ImportSeries/` parallel was NOT
  mirrored (Import Lists are PROJECT.md Out-of-Scope).

## Threat Model References

- **T-07-12 (XSS via lookup term)** — React JSX default escapes `{title}`,
  `{overview}`, etc. No `dangerouslySetInnerHTML`. Safe.
- **T-07-13 (Lookup error disclosure)** — `getErrorMessage(error)` is the
  Mangarr-canonical sanitizer; messages are surface-safe.
- **T-07-14 (Anonymous POST)** — Inherited Mangarr auth middleware (X-Api-Key
  + cookie session) protects `POST /api/v5/manga`; verified Phase 2.

## Cross-References

- [../CLAUDE.md](../CLAUDE.md) — Parent `AddManga/` directory (types + zustand store).
- [../AddManga.ts](../AddManga.ts) — `AddMangaResult` + `AddMangaPayload` types.
- [../addMangaOptionsStore.ts](../addMangaOptionsStore.ts) — zustand store backing the form's persisted defaults.
- [../MangaMonitoringOptionsPopoverContent.tsx](../MangaMonitoringOptionsPopoverContent.tsx) — Help-popover content for the Monitor dropdown.
- Sonarr `AddSeries/AddNewSeries/` (the subtree this was forked from) was deleted in Phase 17.3 Plan 17.3-13 atomic stub-dir delete — see `DIVERGENCE.md`.
- [../../Manga/useManga.ts](../../Manga/useManga.ts) — `useManga` for "already in library" check.
- [../../../../src/Mangarr.Api.V5/Manga/MangaController.cs](../../../../src/Mangarr.Api.V5/Manga/MangaController.cs) — Backend POST /api/v5/manga.
- [../../../../.planning/phases/07-api-v5-frontend-manga-shell/07-06-PLAN.md](../../../../.planning/phases/07-api-v5-frontend-manga-shell/07-06-PLAN.md) — Plan that created this subtree.
- [../../../../.planning/phases/07-api-v5-frontend-manga-shell/07-UI-SPEC.md](../../../../.planning/phases/07-api-v5-frontend-manga-shell/07-UI-SPEC.md) — UI contract for §AddManga, §Empty states, §Form / monitor labels.
