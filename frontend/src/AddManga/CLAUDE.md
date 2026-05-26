# AddManga/

## Purpose

TypeScript types + zustand options store for the **Add Manga** flow — parallel
sibling of `frontend/src/AddSeries/`. Mirrors AddSeries' search-grid + side-panel
shape verbatim per Phase 7 D-04; only the form-field set diverges (5-value
`MangaMonitor`, TranslationProfile/CustomFormatProfile, SearchOnAdd).

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\src\AddManga\`

## Key Files

| File | Purpose |
|------|---------|
| `AddManga.ts` | `AddMangaResult` (lookup-result shape) + `AddMangaPayload` (POST body shape). Mirrors `AddSeries.ts` but with the manga-domain fields per Phase 7 D-04 (no seriesType / seasonFolder; adds `searchForMissingChapters`, `translationProfileId`, `customFormatProfileId`, MangaDex/AniList/MAL identifiers). |
| `addMangaOptionsStore.ts` | Zustand store backed by `localStorage` key **`'add_manga_options'`**. Persists the user's last-used Root Folder, Monitor, TranslationProfile, CustomFormatProfile, SearchOnAdd toggle, Tags across `Add Manga` sessions. Default `monitor: 'all'` per Phase 6 D-03. |
| `MangaMonitoringOptionsPopoverContent.tsx` | 5-entry help-popover content for the Monitor dropdown (Phase 6 D-03 5-value `MangaMonitor`). Mirrors `AddSeries/SeriesMonitoringOptionsPopoverContent.tsx` (which has 11 entries). |

## Subdirectories

### `AddNewManga/` — Search + Add (Plan 07-06)

| File | Purpose |
|------|---------|
| `AddNewManga.tsx` | Page; search input + debounced (500ms) results grid + empty / error states. Routes registered at `/add/manga`. |
| `AddNewMangaSearchResult.tsx` | One result card (cover thumb, title, status pill, author, chapter count, external MangaDex/AniList/MAL link). |
| `AddNewMangaModal.tsx` / `AddNewMangaModalContent.tsx` | Side-panel form: Root Folder, Monitor (5-value), TranslationProfile, CustomFormatProfile, Tags, Search-on-add toggle. |
| `useAddManga.ts` | `useLookupManga(query)` (GET /api/v5/manga/lookup) + `useAddManga()` (POST /api/v5/manga). |
| `*.css` / `*.css.d.ts` | CSS Modules (verbatim copies of AddSeries CSS files; class names auto-scoped). |
| `CLAUDE.md` | Per-directory documentation. |

### `ImportManga/` — Library-Import Flow (Phase 25.1 Plan 25.1-02 + debug-add-import-ui-mismatch fix 2026-05-19)

Sonarr-canonical two-step library-import flow registered at `/add/import` (post-25.1 — supersedes the Phase 25 `InteractiveImportPage.tsx` manual-import-as-page, deleted in Plan 25.1-02 Task 7's atomic AppRoutes swap). Consumes `useRootFolders()` → `rootFolder.unmappedFolders[]` (the existing `RootFolderResource` substrate; no new V5 endpoint shipped — Option B per RESEARCH §5). Per-row defaults come from `addMangaOptionsStore` (D-05' per RESEARCH §6); per-row state lives in the session-only `importMangaStore` (`create<T>()`, not `createOptionsStore` — clears on unmount).

**debug-add-import-ui-mismatch follow-up (2026-05-19):** Phase 25.1 originally shipped these files as a minimal port (no shared visual shell, no per-row select, no manga override dropdown — see `.planning/debug/add-import-ui-mismatch.md` for the full diff). This follow-up re-ports Sonarr's structural shell — `<PageContentFooter>` sticky footer + `<FieldSet>` + shared `<RootFolders/>` + `<FileBrowserModal>` "Choose another folder" + `<SelectProvider>` + per-row `<TableSelectCell>` + `<ImportMangaSelectManga>` search-dropdown trio + `useExistingManga(mangaDexId)` dedupe — while keeping the manga-domain divergences (5-value `MangaMonitor`, `TranslationProfile` + `CustomFormatProfile` instead of `QualityProfile` + `SeriesType` + `SeasonFolder`).

| File | Purpose |
|------|---------|
| `ImportMangaPage.tsx` | Parent Switch host (RR v5 `<Switch>` with `exact` discipline); registers `/add/import` → `ImportMangaSelectFolder` + `/add/import/:rootFolderId` → `ImportManga`. |
| `importMangaStore.ts` | Per-session zustand `create<T>()` (NOT persisted); holds per-row `selectedManga` / `monitor` / `translationProfileId` / `customFormatProfileId` overrides. Cleared on `ImportMangaPage` unmount per RESEARCH §2.6. |
| `ImportMangaSelectFolder/ImportMangaSelectFolder.tsx` | `/add/import` sub-page — centered header + 3-bullet tips + `<FieldSet legend="Root Folders">` wrapping the shared `<RootFolders/>` table + primary "Choose another folder" `<Button>` + `<FileBrowserModal>` for adding a new root folder + `addError` `<Alert>`. The bespoke `ImportMangaSelectFolderRow.tsx` was deleted in the debug-add-import-ui-mismatch fix — the shared `RootFolderRow.tsx` (Path / FreeSpace / UnmappedFolders / delete-X anchor link to `/add/import/:id`) replaces it. |
| `ImportManga/ImportManga.tsx` | `/add/import/:rootFolderId` sub-page — wrapped in `<SelectProvider>`; per-folder scan table; empty-state is an `<Alert kind={kinds.INFO}>` with `AllMangaInRootFolderHaveBeenImported`; `<ImportMangaFooter>` lives OUTSIDE `<PageContentBody>` so it sticks. |
| `ImportManga/ImportMangaRow.tsx` | Per-row — `<TableSelectCell>` checkbox wired to `useSelect<ImportMangaItem>()`, Monitor / TranslationProfile / CustomFormatProfile selects, `<ImportMangaSelectManga>` override-dropdown, `useExistingManga(selectedManga?.mangaDexId)` greys out + disables already-imported rows. |
| `ImportManga/ImportMangaFooter.tsx` | `<PageContentFooter>` sticky shell with 3 bulk selects (writes only to SELECTED rows via `getSelectedIds()`), "Import {selectedCount} Manga" SpinnerButton, StartProcessing / CancelProcessing buttons + ImportErrors `<Popover>`. |
| `ImportManga/SelectManga/ImportMangaSelectManga.tsx` | Per-row search-dropdown popover (peer of Sonarr's `ImportSeriesSelectSeries`) — floating-ui-positioned, debounced text input, scrollable result list. |
| `ImportManga/SelectManga/ImportMangaSearchResult.tsx` | One result card (peer of `ImportSeriesSearchResult`) — clickable `<ImportMangaTitle>` + external-link icon to `https://mangadex.org/title/{mangaDexId}`. |
| `ImportManga/SelectManga/ImportMangaTitle.tsx` | Selected-title chip (peer of `ImportSeriesTitle`) — title + year + metadataSource label (MangaDex / AniList / MyAnimeList) + "Existing" warning chip when `useExistingManga` matches. |
| `*.css` / `*.css.d.ts` | CSS Modules per Mangarr frontend convention (plain `.css` extension; webpack auto-scopes; `.css.d.ts` is build-emitted). |

## Patterns / Conventions

- **localStorage key:** `add_manga_options` (NOT `add_series_options`). See
  verification grep in `07-03-SUMMARY.md` confirming no collision.
- **5-value `MangaMonitor`** — see `Manga/Manga.ts`. Default `'all'` matches the
  AddSeries default and is canonical per UI-SPEC §AddManga.
- **No `qualityProfileId`** — Mangarr replaced Quality Profiles with the
  TranslationProfile + CustomFormatProfile two-layer model per
  `.planning/decisions/cf-only-walkthrough.md`. Default IDs `0` mean
  "fall back to Config.DefaultTranslationProfileId / DefaultCustomFormatProfileId"
  (Phase 5 D-11 default-seeded entities).
- **`searchForMissingChapters`** is the SearchOnAdd toggle (Phase 6 D-06) —
  controls whether `MangaSearchCommand` fires immediately after the new manga
  is persisted.

## Manga Adaptation Notes

This directory IS the manga adaptation of `frontend/src/AddSeries/`. Phase 8
cleanup will collapse the two when `AddSeries/` deletes.

## Cross-References

- Sonarr `AddSeries/` (the sibling this flow was forked from) was deleted in Phase 17.3 Plan 17.3-13 atomic stub-dir delete — see `DIVERGENCE.md`. This `AddManga/` subtree is now the single canonical add flow.
- [../Manga/CLAUDE.md](../Manga/CLAUDE.md) — Source of `MangaMonitor` / `MangaImage` / `MangaStatus` types.
- [../Helpers/Hooks/useOptionsStore.ts](../Helpers/Hooks/useOptionsStore.ts) — `createOptionsStore<T>(name, state, options?)` factory.
- [../../../src/Mangarr.Api.V5/Manga/MangaController.cs](../../../src/Mangarr.Api.V5/Manga/MangaController.cs) — Backend POST `/api/v5/manga` consumer of `AddMangaPayload`.
- [../../../.planning/phases/07-api-v5-frontend-manga-shell/07-RESEARCH.md](../../../.planning/phases/07-api-v5-frontend-manga-shell/07-RESEARCH.md) — Lock #11 / #12 (form field set), Example 4 (addMangaOptionsStore pattern).
