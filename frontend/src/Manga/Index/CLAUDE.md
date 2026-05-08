# Manga/Index/

## Purpose

The manga library page (UI-03). Renders the 3-mode (Posters / Overview / Table)
toggle that sits at `/manga`. Mirrors `frontend/src/Series/Index/` verbatim per
Phase 7 D-02 + Lock #10 with the manga-domain divergences (no seasons, no
episodes, manga-shape filter/sort columns).

**Absolute Path:** `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\src\Manga\Index`

## File Tree

82 files (Mangarr's `Series/Index/` had 95; 13 omitted per Lock #10):

```
Manga/Index/
├── MangaIndex.tsx (+ .css + .css.d.ts)            ← page entry, 3-mode dispatch
├── MangaIndexFooter.tsx (+ .css + .css.d.ts)      ← summary stats footer
├── MangaIndexFilterModal.tsx                       ← custom-filter editor modal
├── MangaIndexRefreshMangaButton.tsx                ← toolbar refresh button
├── useMangaIndexItem.ts                            ← per-row hook (manga + qp)
├── Menus/MangaIndex{Filter,Sort,View}Menu.tsx
├── Posters/
│   ├── Options/MangaIndexPosterOptionsModal{,Content}.tsx
│   ├── MangaIndexPoster.tsx (+ .css + .css.d.ts)
│   ├── MangaIndexPosterInfo.tsx (+ .css + .css.d.ts)
│   └── MangaIndexPosters.tsx
├── Overview/
│   ├── Options/MangaIndexOverviewOptionsModal{,Content}.tsx
│   ├── MangaIndexOverview.tsx (+ .css + .css.d.ts)
│   ├── MangaIndexOverviewInfo.tsx (+ .css + .css.d.ts)
│   ├── MangaIndexOverviewInfoRow.tsx (+ .css + .css.d.ts)
│   └── MangaIndexOverviews.tsx
├── ProgressBar/
│   └── MangaIndexProgressBar.tsx (+ .css + .css.d.ts)
├── Select/
│   ├── Delete/{Files/}DeleteMangaModal{,Content}.tsx + MangaDeleteList.tsx
│   ├── Edit/EditMangaModal{,Content}.tsx
│   ├── Organize/OrganizeMangaModal{,Content}.tsx
│   ├── Tags/TagsModal{,Content}.tsx
│   ├── MangaIndexPosterSelect.tsx (+ .css + .css.d.ts)
│   └── MangaIndexSelect{All,Mode}{Button,MenuItem}.tsx + MangaIndexSelectFooter.{tsx,css,css.d.ts}
└── Table/
    ├── MangaIndexRow.tsx (+ .css + .css.d.ts)
    ├── MangaIndexTable.tsx (+ .css + .css.d.ts)
    ├── MangaIndexTableHeader.tsx (+ .css + .css.d.ts)
    ├── MangaIndexTableOptions.tsx
    ├── MangaStatusCell.tsx (+ .css + .css.d.ts)
    └── hasGrowableColumns.ts
```

## Lock #10 Omissions

13 files dropped from the verbatim Mangarr port:

| Path | Reason |
|------|--------|
| `Select/SeasonPass/ChangeMonitoringModal{,Content}.{tsx,css,css.d.ts}` (4 files) | Manga has no seasons (PROJECT.md Volumes/Seasons Out-of-Scope) |
| `Select/SeasonPass/SeasonDetails.{tsx,css,css.d.ts}` (3 files) | ditto |
| `Select/SeasonPass/SeasonPassSeason.{tsx,css,css.d.ts}` (3 files) | ditto |
| `Table/SeasonsCell.{tsx,css,css.d.ts}` (3 files) | The `seasonCount` column renders an em-dash inline instead |

## Patterns / Conventions

- **Sibling-divergence comment block** at the top of `MangaIndex.tsx` per
  Pattern S2 (RESEARCH.md). Phase 8 collapse note included.
- **3-view toggle** dispatched via `getViewComponent(view)`:
  - `'posters'` → `MangaIndexPosters` (default per Lock #2)
  - `'overview'` → `MangaIndexOverviews`
  - `'table'` → `MangaIndexTable`
- **State store:** `Manga/mangaOptionsStore.ts` (Plan 07-03; column-array filled
  here in Plan 07-04 with `status / sortTitle / originalLanguage /
  translationProfileId / customFormatProfileId / chapterCount /
  chapterProgress / added / year / path / sizeOnDisk / genres / tags /
  actions`). LocalStorage key: `manga_options`.
- **Cover-art URL** read off `manga.images[].url` per Lock #3 — already
  rewritten by `MangaController.MapResource → _coverMapper.ConvertToLocalUrls`
  to `/MediaCover/manga/{id}/poster.jpg`.
- **SignalR cache key:** `['/manga']` (Plan 07-02 contract). Backend
  `manga` resource pushes invalidate the manga list within 1s.

## Mangarr Inheritance Notes

A handful of inherited components reach back into `frontend/src/Series/` for
peer modals + helpers that Plan 07-04 chose not to fork in scope:

| Manga consumer | Series peer used |
|----------------|------------------|
| `Select/Delete/DeleteMangaModal.tsx` | `Series/Delete/DeleteSeriesModal` (TV-shape; `seriesId` prop) |
| `Select/Edit/EditMangaModal.tsx` | `Series/Edit/EditSeriesModal` (ditto) |
| `Posters/MangaIndexPoster.tsx`, `Overview/MangaIndexOverview.tsx`, `Table/MangaIndexRow.tsx` | call into the modals above with `seriesId={mangaId}` |
| `ProgressBar/MangaIndexProgressBar.tsx` | `Utilities/Series/getProgressBarKind` (status arg widened to `string`) |
| `Table/MangaStatusCell.tsx` | `Series/SeriesStatus.getSeriesStatusDetails` (status arg widened to `string`) |
| `Overview/MangaIndexOverviews.tsx`, `Posters/MangaIndexPosters.tsx`, `Table/MangaIndexTable.tsx` | `Utilities/Array/getIndexOfFirstCharacter` (now generic over any `{ sortTitle: string }`) |

Phase 8 cleanup will collapse Series/ + the dual-codepath. The Phase 7-friendly
typing changes to `getProgressBarKind` / `getSeriesStatusDetails` /
`getIndexOfFirstCharacter` were widening-only and preserve TV behaviour.

## Cross-References

- [Series/Index/CLAUDE.md](../../Series/Index/CLAUDE.md) — verbatim source
- [Manga/CLAUDE.md](../CLAUDE.md) — peer files (useManga, NoManga, MangaPoster, …)
- [.planning/phases/07-api-v5-frontend-manga-shell/07-RESEARCH.md](../../../../.planning/phases/07-api-v5-frontend-manga-shell/07-RESEARCH.md) — Lock #2, Lock #3, Lock #10
- [.planning/phases/07-api-v5-frontend-manga-shell/07-04-PLAN.md](../../../../.planning/phases/07-api-v5-frontend-manga-shell/07-04-PLAN.md)
