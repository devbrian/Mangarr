# Manga/

## Purpose

TypeScript types, hooks, components, and the zustand options store for the
manga domain — the canonical frontend manga module post-Phase-17.3. Backs the
Manga library page (`Index/`), the Manga details page (`Details/`), and
downstream Activity / Wanted consumers (Phase 7 Plans 07-09 / 07-10).

Historical note: prior to Plan 17.3-13 this directory was the "parallel
sibling of `frontend/src/Series/`" — the Series/ directory existed as a set
of thin re-export stubs from Phase 15 Plan 15-12 to keep verbatim-inherited
TV consumers compiling during the cutover. Plan 17.3-13 (D-09/D-10) atomic
stub-dir delete retired the Series/ subtree; this directory is now the
single canonical home for manga frontend types + hooks + components.

**Absolute Path:** `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\src\Manga\`

## Key Files

| File | Purpose |
|------|---------|
| `Manga.ts` | `Manga` interface (extends `ModelBase`) + `MangaMonitor` 5-value type literal (per Phase 6 D-03 — `'all' \| 'future' \| 'missing' \| 'latest' \| 'none'`) + `MangaStatus` + `MangaImage`. Phase 17.3 Plan 17.3-12 (D-13, completed 2026-05-11) stripped the Sonarr-shape carry-over fields (`network`, `originalCountry`, `originalLanguage`, `firstAired`, `lastAired`, `previousAiring`, `nextAiring`, `seriesType`, `seasonFolder`, `seasons`, `ended`, `runtime`, `imdbId`, `tvdbId`, `tvMazeId`, `tvRageId`, `tmdbId`, `useSceneNumbering`, and the `Statistics` episode/season subfields). The interface is now manga-shape only. Mirrors the V5 backend `MangaResource` (`src/Mangarr.Api.V5/Manga/MangaResource.cs`). |
| `MangaStatus.ts` | `MANGA_STATUS_VALUES` constant array + `isMonitorableStatus()` helper. Originally sibling of `Series/SeriesStatus.ts`; that stub was deleted in Plan 17.3-13 atomic stub-dir delete. `MangaStatus.ts` is now standalone. |
| `mangaOptionsStore.ts` | Zustand store backed by `localStorage` key **`'manga_options'`**. 3-view toggle (`posters` / `overview` / `table`), default `view: 'posters'` per Lock #2. Exports `useMangaOptions` / `setMangaOptions` / `useMangaOption` / `setMangaOption` / `setMangaSort` / `getMangaOptions` + per-subview helpers (`useMangaPosterOptions` / `setMangaPosterOptions`, `useMangaOverviewOptions` / `setMangaOverviewOptions`, `useMangaTableOptions` / `setMangaTableOptions`, `useMangaDeleteOptions` / `setMangaDeleteOptions`). The `columns` array carries the manga-canonical column set (Plan 07-04 + Plan 17.3-08 D-14 per-component fork): status, sortTitle, translationProfileId, customFormatProfileId, chapterProgress, chapterCount, scanlationGroups, translatedLanguages, metadataSource, contentRating, added, year, path, sizeOnDisk, genres, ratings, certification, tags, actions. (Two of the inherited entries — `originalCountry` and `originalLanguage` — remain registered in the column registry for back-compat with persisted Zustand state; they render an em-dash because the `Manga` interface no longer carries those fields per Plan 17.3-12 D-13.) |
| `useManga.ts` | The canonical manga data-hooks module. Exports the default `useManga()` hook + `useMangaIndex` (filter+sort), `useSingleManga`, `useHasManga`, `useMultipleManga`, `useSaveManga`, `useDeleteManga`, `useToggleMangaMonitored`, `useUpdateMangaMonitor` (stub), `useSaveMangaEditor`, `useBulkDeleteManga`, plus the `FILTERS` / `FILTER_BUILDER` arrays. **React Query key: `['/manga']`** — Plan 07-02 SignalR contract. Sonarr `Series/useSeries.ts` re-export stub deleted in Plan 17.3-13. |
| `useMangaQualityProfile.ts` | Stub returning `undefined` for the inherited Index columns (`MangaIndexPosterInfo` / `MangaIndexOverviewInfo` / `MangaIndexRow` reference `qualityProfile?.name` and gracefully render empty). Previously delegated to `useQualityProfile`, which fired `GET /api/v5/qualityprofile` 404 on every home-page mount (Phase 5 D-01 deleted `QualityProfileController`); home-404s-queue-qualityprofile fix (2026-05-09) collapsed the delegation. Replacement candidate: `useMangaTranslationProfile` (Phase 5 D-04). |
| `NoManga.tsx` | Empty-state for the library page. UI-SPEC §Empty States locked copy: heading `No manga added yet`, body links to `/add/manga` via `Add New Manga` CTA. Styles ported in-tree (Sonarr `Series/NoSeries.css` deleted in Plan 17.3-13). |
| `MangaPoster.tsx` | Renders the cover art (138 px portrait per UI-SPEC §Visual Hierarchy). Reads `manga.images[].url` per Lock #3 — already rewritten by `MangaController.MapResource → _coverMapper.ConvertToLocalUrls` to `/MediaCover/manga/{id}/poster.jpg`. |
| `MangaImage.tsx` | Lazy-loaded image with retry-on-error + size-suffix URL rewrite. Sonarr `MediaCover` serves both `poster.jpg` and `poster-{size}.jpg`. |
| `MangaBanner.tsx` | Banner-aspect variant (35 / 70 px). |
| `MangaTitleLink.tsx` | `<Link to={'/manga/' + titleSlug}>{title}</Link>` wrapper for poster + table title cells. |
| `MangaGenres.tsx` | Genre label + tooltip overflow component (mirror of `Series/SeriesGenres.tsx`). |
| `Index/` | The 3-mode (Posters / Overview / Table) library page (UI-03). See [Index/CLAUDE.md](./Index/CLAUDE.md). |
| `Details/` | Manga details page (Overview / Chapters / Files / History / Search tabs). See [Details/CLAUDE.md](./Details/CLAUDE.md). |
| `Edit/` | Single-manga Edit modal (PR #27 — `fix(manga-edit-button-no-op)`; extended for Issue #28 to expose `MonitorNewItems` + `TranslationProfileId` + `CustomFormatProfileId` alongside `Monitored` + `Tags`; extended for issue #81 to add editable Path + RootFolder picker button + MoveMangaModal confirmation gate). Wired into the Edit toolbar button on `Details/MangaDetails.tsx`. |
| `Edit/RootFolder/` | Destination-folder picker shown when the user clicks the root-folder button on the single-edit Path field (issue #81 — 2026-05-13). `RootFolderModal.tsx` + `RootFolderModalContent.tsx` — 1:1 port of upstream `Series/Edit/RootFolder/` with `seriesId -> mangaId` rename; hits `GET /api/v5/manga/{id}/folder` via `MangaFolderController` (Phase 13 Plan 13-05 D-13-04 forward-prophylactic, finally reaching its caller). |
| `MoveManga/` | Confirmation dialog shown when changing root folder in either single-edit or bulk-edit modal (issue #81 — 2026-05-13). `MoveMangaModal.tsx` + `.css` — 1:1 port of upstream `Series/MoveSeries/MoveSeriesModal.tsx` (deleted in Phase 17.3 Plan 17.3-03 D-11 as a no-op stub before a real port could land). Three buttons: Cancel / DontMoveFiles (DB only) / DANGER MoveFiles (backend enqueues MoveMangaCommand or BulkMoveMangaCommand). |
| `Delete/` | Single-manga Delete modal (`fix(manga-delete-button-no-op)`). Wired into the Delete toolbar button on `Details/MangaDetails.tsx`. See [Delete/CLAUDE.md](./Delete/CLAUDE.md). |

## Patterns / Conventions

- **localStorage key:** `manga_options` (NOT `series_options`). See verification grep
  in `07-03-SUMMARY.md` confirming no collision.
- **5-value `MangaMonitor`** — Phase 6 D-03 is locked at `'all' | 'future' | 'missing' | 'latest' | 'none'`. Do NOT extend this without a documented decision.
- **Default view = `'posters'`** — Lock #2 from `07-RESEARCH.md`.
- **Column set is manga-canonical** (Plan 07-04 baseline + Plan 17.3-08 D-14
  per-component fork — Series/ stub-dir deleted in Plan 17.3-13, so the
  earlier "diverges from Series" framing is moot): translationProfileId /
  customFormatProfileId / chapterCount / chapterProgress / scanlationGroups /
  translatedLanguages / metadataSource / contentRating / status / sortTitle /
  added / year / path / sizeOnDisk / genres / ratings / certification / tags /
  actions. TV-shape columns (seriesType, network, qualityProfileId,
  nextAiring, previousAiring, seasonCount, seasonFolder, episodeProgress,
  episodeCount, latestSeason, useSceneNumbering, monitorNewItems,
  episodeFileQualities, releaseGroups, releaseTypes, averageSizePerEpisode)
  were never registered or were dropped in Plan 17.3-08 D-14.
- **React Query key contract** (Plan 07-02): list reads use `['/manga']`,
  detail reads use `['/manga', id]`. The SignalR `manga` resource handler
  invalidates `['/manga']` on every backend update.
- **Cover URL** sourced from `manga.images[].url` (already locally rewritten
  by the backend `_coverMapper.ConvertToLocalUrls`); fall back to
  `/Content/Images/poster-placeholder.png` if none.
- **Sibling-divergence comments (Pattern S2)** — every file here carries the
  `// Sonarr divergence: NEW manga sibling per Phase 7 D-NN — see DIVERGENCE.md.` header.

## Phase 16.1 chapter list (post-2026-05-10 revert)

The chapter table renders **one row per canonical Chapter** (Phase 16 STRUCT-09 + D-03 column-set, preserved through Phase 16.1):

- **Column count: 5.** `MangaDetailsChapters.tsx` `DEFAULT_COLUMNS` carries `monitored`, `chapterNumber`, `title`, `status`, `actions`. Phase 16's column drop survived the Phase 16.1 revert; no `releases[]`-driven columns ever shipped.
- **Status pill carries aggregate state** (Sonarr-canonical predicate post-Phase-16.1 REVERT-05 + REVERT-07; Pitfall 5 — 6-state precedence preserved):
  - **File:** chapter has been downloaded (`chapterFileId != null`).
  - **Missing:** `monitored && !hasFile` (Sonarr-canonical mirror of `Episode.Monitored && EpisodeFileId == 0`). Pill text is the literal `'Missing'` for max Sonarr parity (per Phase 16.1 Wave 1 lock + PATTERNS.md Pitfall 8).
  - **Future-dated:** `firstReleaseDate > today` (unaired-future style; matches existing `ChapterMonitoredService:80` filter).
- **6-state precedence preserved** (Pitfall 5 — failed > blocklisted > have-file > queued > wanted/missing > unmonitored). The Phase 16 D-04 `releases.length === 0` alias-flip is GONE; Phase 16.1 reverted the predicate to the Sonarr-canonical `monitored && !hasFile` shape.

**Wire shape consumed:** Frontend `Chapter.ts` mirrors API V5 `ChapterResource` post-Phase-16.1 — keeps `firstReleaseDate?: string` (Phase 16 D-02 — survived the revert per SPEC Out of Scope; Sonarr-mirror of `Episode.AirDateUtc`); does NOT carry a `releases[]` field. The pre-Phase-16 per-language fields (`translatedLanguage`, `scanlationGroup`, `isSynthetic`, `releaseDate`) remain dropped — Phase 16.1 REVERT-07 confirms `IsSynthetic` stays gone. The Phase 16-introduced `frontend/src/Chapter/ChapterRelease.ts` was DELETED in Phase 16.1 Wave 1 (REVERT-07 acceptance).

**Per-translation data placement (Phase 16.1 Sonarr-canonical pattern):** Per-translation language + scanlation-group axes live on `ChapterFile` post-import (mirrors Sonarr's `EpisodeFile.Languages` + `EpisodeFile.ReleaseGroup` slot pair). The frontend reads these fields off `ChapterFileResource.translatedLanguage` + `ChapterFileResource.scanlationGroup` (Phase 16.1 D-05 wire-side rename — `scanlationGroup` is the canonical "release group" axis for manga; D-06 marker on the C# property).

**Historical note:** Phase 16 (closed 2026-05-09) introduced a `ChapterRelease.ts` type + `releases[]` collection on `Chapter.ts` to carry per-(language, scanlation-group) translation metadata, with `ChapterStatus.tsx` predicate alias-flipping on `releases.length === 0`. Phase 16.1 reverted all of that in favor of the Sonarr-canonical pattern. See `.planning/phases/16.1-revert-chapterrelease-adopt-sonarr-canonical-translation-pat/16.1-SUMMARY.md` for the full record.

## Manga Adaptation Notes

Phase 17.3 Plan 17.3-13 (D-09/D-10) atomic stub-dir delete completed the
`Series/` → `Manga/` cutover. The pre-Phase-17.3 inheritance pattern
(Manga/Index/ components reaching back into `Series/Delete/DeleteSeriesModal`,
`Series/Edit/EditSeriesModal`, `Utilities/Series/getProgressBarKind`,
`Series/SeriesStatus.getSeriesStatusDetails`) is GONE — Plan 17.3-08 (D-14)
landed per-component forks for the inherited Index components
(MangaIndexRow / MangaIndexOverviewInfo / MangaIndexPoster /
EditMangaModalContent), and the Series/* peers were deleted in Plan
17.3-13. The single-manga Edit + Delete modals shipped as dedicated peers
under `Manga/Edit/` (PR #27) + `Manga/Delete/` ahead of the stub-dir
delete.

## Cross-References

- [../Chapter/CLAUDE.md](../Chapter/CLAUDE.md) — Chapter sibling type.
- [../AddManga/CLAUDE.md](../AddManga/CLAUDE.md) — Add-manga flow + form-state store.
- [../typings/CLAUDE.md](../typings/CLAUDE.md) — Cross-cutting typing folder (`MangaQueueItem.ts`, `ChapterHistory.ts`, `MangaBlocklist.ts`).
- [../Helpers/Hooks/useOptionsStore.ts](../Helpers/Hooks/useOptionsStore.ts) — `createOptionsStore<T>(name, state, options?)` factory.
- [../../../src/Mangarr.Api.V5/Manga/MangaResource.cs](../../../src/Mangarr.Api.V5/Manga/MangaResource.cs) — Backend resource shape this type mirrors.
- [../../../.planning/phases/07-api-v5-frontend-manga-shell/07-RESEARCH.md](../../../.planning/phases/07-api-v5-frontend-manga-shell/07-RESEARCH.md) — Lock #2 (default view), Lock #3 (cover URL), Example 4 (column set), Lock #10 (verbatim port file tree).
- [../../../.planning/phases/07-api-v5-frontend-manga-shell/07-04-PLAN.md](../../../.planning/phases/07-api-v5-frontend-manga-shell/07-04-PLAN.md) — Originating plan.
- [../../../.planning/phases/17.3-domain-rename-residue-sweep-pre-v1-sweep-tv-term-residue-pha/17.3-12-PLAN.md](../../../.planning/phases/17.3-domain-rename-residue-sweep-pre-v1-sweep-tv-term-residue-pha/17.3-12-PLAN.md) — D-13 Manga.ts trim.
- [../../../.planning/phases/17.3-domain-rename-residue-sweep-pre-v1-sweep-tv-term-residue-pha/17.3-13-PLAN.md](../../../.planning/phases/17.3-domain-rename-residue-sweep-pre-v1-sweep-tv-term-residue-pha/17.3-13-PLAN.md) — D-09/D-10 atomic stub-dir delete.
