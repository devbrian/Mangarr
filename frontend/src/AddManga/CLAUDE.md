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

- [../AddSeries/CLAUDE.md](../AddSeries/CLAUDE.md) — Sibling AddSeries flow (Phase 8 cleanup target).
- [../Manga/CLAUDE.md](../Manga/CLAUDE.md) — Source of `MangaMonitor` / `MangaImage` / `MangaStatus` types.
- [../Helpers/Hooks/useOptionsStore.ts](../Helpers/Hooks/useOptionsStore.ts) — `createOptionsStore<T>(name, state, options?)` factory.
- [../../../src/Mangarr.Api.V5/Manga/MangaController.cs](../../../src/Mangarr.Api.V5/Manga/MangaController.cs) — Backend POST `/api/v5/manga` consumer of `AddMangaPayload`.
- [../../../.planning/phases/07-api-v5-frontend-manga-shell/07-RESEARCH.md](../../../.planning/phases/07-api-v5-frontend-manga-shell/07-RESEARCH.md) — Lock #11 / #12 (form field set), Example 4 (addMangaOptionsStore pattern).
