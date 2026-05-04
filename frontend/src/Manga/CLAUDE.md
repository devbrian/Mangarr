# Manga/

## Purpose

TypeScript types, hooks, components, and the zustand options store for the
manga domain — the parallel sibling of `frontend/src/Series/`. Backs the
Manga library page (`Index/`), the Manga details page (Plan 07-05 — pending),
and downstream Activity / Wanted consumers (Plans 07-09 / 07-10).

**Absolute Path:** `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\src\Manga\`

## Key Files

| File | Purpose |
|------|---------|
| `Manga.ts` | `Manga` interface (extends `ModelBase`) + `MangaMonitor` 5-value type literal (per Phase 6 D-03 — `'all' \| 'future' \| 'missing' \| 'latest' \| 'none'`) + `MangaStatus` + `MangaImage` + Sonarr-shape carry-over fields (statistics / seasons / originalLanguage / network / nextAiring / etc.) typed as optional so the verbatim-inherited Index components from Plan 07-04 compile. Mirrors the V5 backend `MangaResource` (`src/Sonarr.Api.V5/Manga/MangaResource.cs`). |
| `MangaStatus.ts` | `MANGA_STATUS_VALUES` constant array + `isMonitorableStatus()` helper. Sibling of `Series/SeriesStatus.ts`. |
| `mangaOptionsStore.ts` | Zustand store backed by `localStorage` key **`'manga_options'`**. 3-view toggle (`posters` / `overview` / `table`), default `view: 'posters'` per Lock #2. Exports `useMangaOptions` / `setMangaOptions` / `useMangaOption` / `setMangaOption` / `setMangaSort` / `getMangaOptions` + per-subview helpers (`useMangaPosterOptions` / `setMangaPosterOptions`, `useMangaOverviewOptions` / `setMangaOverviewOptions`, `useMangaTableOptions` / `setMangaTableOptions`, `useMangaDeleteOptions` / `setMangaDeleteOptions`). The 21-entry `columns` array (Plan 07-04 — RESEARCH Example 4): status, sortTitle, originalCountry, originalLanguage, translationProfileId, customFormatProfileId, chapterProgress, chapterCount, scanlationGroups, translatedLanguages, metadataSource, contentRating, added, year, path, sizeOnDisk, genres, ratings, certification, tags, actions. |
| `useManga.ts` | Mirrors `Series/useSeries.ts`. Exports the default `useManga()` hook + `useMangaIndex` (filter+sort), `useSingleManga`, `useHasManga`, `useMultipleManga`, `useSaveManga`, `useDeleteManga`, `useToggleMangaMonitored`, `useUpdateMangaMonitor` (stub), `useSaveMangaEditor`, `useBulkDeleteManga`, plus the `FILTERS` / `FILTER_BUILDER` arrays. **React Query key: `['/manga']`** — Plan 07-02 SignalR contract. |
| `useMangaQualityProfile.ts` | Stub mirror of `Series/useSeriesQualityProfile.ts` for the inherited Index columns. Reads `manga.qualityProfileId` (Sonarr-shape carry-over). Phase 8 collapses or replaces with `useMangaTranslationProfile`. |
| `NoManga.tsx` | Empty-state for the library page. UI-SPEC §Empty States locked copy: heading `No manga added yet`, body links to `/add/manga` via `Add New Manga` CTA. Reuses `Series/NoSeries.css` styles. |
| `MangaPoster.tsx` | Renders the cover art (138 px portrait per UI-SPEC §Visual Hierarchy). Reads `manga.images[].url` per Lock #3 — already rewritten by `MangaController.MapResource → _coverMapper.ConvertToLocalUrls` to `/MediaCover/manga/{id}/poster.jpg`. |
| `MangaImage.tsx` | Lazy-loaded image with retry-on-error + size-suffix URL rewrite. Sonarr `MediaCover` serves both `poster.jpg` and `poster-{size}.jpg`. |
| `MangaBanner.tsx` | Banner-aspect variant (35 / 70 px). |
| `MangaTitleLink.tsx` | `<Link to={'/manga/' + titleSlug}>{title}</Link>` wrapper for poster + table title cells. |
| `MangaGenres.tsx` | Genre label + tooltip overflow component (mirror of `Series/SeriesGenres.tsx`). |
| `Index/` | The 3-mode (Posters / Overview / Table) library page (UI-03). See [Index/CLAUDE.md](./Index/CLAUDE.md). |
| `Details/` | (Plan 07-05 — pending) Manga details page (Overview / Chapters / Files / History / Search tabs). |

## Patterns / Conventions

- **localStorage key:** `manga_options` (NOT `series_options`). See verification grep
  in `07-03-SUMMARY.md` confirming no collision.
- **5-value `MangaMonitor`** — Phase 6 D-03 is locked at `'all' | 'future' | 'missing' | 'latest' | 'none'`. Do NOT extend this without a documented decision.
- **Default view = `'posters'`** — Lock #2 from `07-RESEARCH.md`.
- **Column set diverges from Series** by dropping seriesType / network /
  qualityProfileId / nextAiring / previousAiring / seasonCount / seasonFolder /
  episodeProgress / episodeCount / latestSeason / useSceneNumbering /
  monitorNewItems / episodeFileQualities / releaseGroups / releaseTypes /
  averageSizePerEpisode and adding translationProfileId /
  customFormatProfileId / chapterCount / chapterProgress / scanlationGroups /
  translatedLanguages / metadataSource / contentRating.
- **React Query key contract** (Plan 07-02): list reads use `['/manga']`,
  detail reads use `['/manga', id]`. The SignalR `manga` resource handler
  invalidates `['/manga']` on every backend update.
- **Cover URL** sourced from `manga.images[].url` (already locally rewritten
  by the backend `_coverMapper.ConvertToLocalUrls`); fall back to
  `/Content/Images/poster-placeholder.png` if none.
- **Sibling-divergence comments (Pattern S2)** — every file here carries the
  `// Sonarr divergence: NEW manga sibling per Phase 7 D-NN — see DIVERGENCE.md.` header.

## Manga Adaptation Notes

This directory IS the manga adaptation of `frontend/src/Series/`. Phase 8 cutover
will collapse the two when `Series/` deletes — at which point all the
`// Phase 8 cleanup: collapse with Series when Tv/ deletes.` markers in this
directory point Phase 8's executor at the canonical sibling to merge.

A handful of inherited Manga/Index/ components reach back into Series/ for
peer modals (`Series/Delete/DeleteSeriesModal`, `Series/Edit/EditSeriesModal`)
+ helpers (`Utilities/Series/getProgressBarKind`,
`Series/SeriesStatus.getSeriesStatusDetails`) — see Index/CLAUDE.md
"Sonarr Inheritance Notes" for the full table.

## Cross-References

- [../Series/CLAUDE.md](../Series/CLAUDE.md) — Sibling Series feature (Phase 8 cleanup target).
- [../Chapter/CLAUDE.md](../Chapter/CLAUDE.md) — Chapter sibling type.
- [../AddManga/CLAUDE.md](../AddManga/CLAUDE.md) — Add-manga flow + form-state store (pending).
- [../typings/CLAUDE.md](../typings/CLAUDE.md) — Cross-cutting typing folder (`MangaQueueItem.ts`, `ChapterHistory.ts`, `MangaBlocklist.ts`).
- [../Helpers/Hooks/useOptionsStore.ts](../Helpers/Hooks/useOptionsStore.ts) — `createOptionsStore<T>(name, state, options?)` factory.
- [../../../src/Sonarr.Api.V5/Manga/MangaResource.cs](../../../src/Sonarr.Api.V5/Manga/MangaResource.cs) — Backend resource shape this type mirrors.
- [../../../.planning/phases/07-api-v5-frontend-manga-shell/07-RESEARCH.md](../../../.planning/phases/07-api-v5-frontend-manga-shell/07-RESEARCH.md) — Lock #2 (default view), Lock #3 (cover URL), Example 4 (column set), Lock #10 (verbatim port file tree).
- [../../../.planning/phases/07-api-v5-frontend-manga-shell/07-04-PLAN.md](../../../.planning/phases/07-api-v5-frontend-manga-shell/07-04-PLAN.md) — This plan.
