# Manga/Index/

## Purpose

The manga library page (UI-03). Renders the 3-mode (Posters / Overview / Table)
toggle that sits at `/manga`. Originally mirrored `frontend/src/Series/Index/`
verbatim per Phase 7 D-02 + Lock #10 with the manga-domain divergences (no
seasons, no episodes, manga-shape filter/sort columns). Phase 17.3 Plan 17.3-08
(D-14) landed per-component forks on the Index components
(MangaIndexRow / MangaIndexOverviewInfo / MangaIndexPoster /
EditMangaModalContent); Plan 17.3-13 atomic stub-dir delete retired the
`Series/Index/` peer. This directory is now the single canonical home for
the manga library page.

**Absolute Path:** `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\src\Manga\Index`

## File Tree

82 files (Sonarr's `Series/Index/` had 95; 13 omitted per Phase 7 Lock #10
— historical reference; `Series/Index/` itself was deleted in Plan 17.3-13
atomic stub-dir delete):

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

## Lock #10 Omissions (historical)

13 files dropped from the verbatim Sonarr port at Phase 7 — historical
record (the Sonarr `Series/Index/` source tree no longer exists in the
working copy per Plan 17.3-13 atomic stub-dir delete):

| Path | Reason |
|------|--------|
| `Select/SeasonPass/ChangeMonitoringModal{,Content}.{tsx,css,css.d.ts}` (4 files) | Manga has no seasons (PROJECT.md Volumes/Seasons Out-of-Scope) |
| `Select/SeasonPass/SeasonDetails.{tsx,css,css.d.ts}` (3 files) | ditto |
| `Select/SeasonPass/SeasonPassSeason.{tsx,css,css.d.ts}` (3 files) | ditto |
| `Table/SeasonsCell.{tsx,css,css.d.ts}` (3 files) | `seasonCount` column was removed in Plan 17.3-08 D-14 per-component fork (no em-dash placeholder ships in v1) |

## Patterns / Conventions

- **Sibling-divergence comment block** at the top of `MangaIndex.tsx` per
  Pattern S2 (RESEARCH.md). Phase 8 collapse note included.
- **3-view toggle** dispatched via `getViewComponent(view)`:
  - `'posters'` → `MangaIndexPosters` (default per Lock #2)
  - `'overview'` → `MangaIndexOverviews`
  - `'table'` → `MangaIndexTable`
- **State store:** `Manga/mangaOptionsStore.ts` (Plan 07-03; column-array filled
  here in Plan 07-04 and trimmed by Plan 17.3-08 D-14 to: `status / sortTitle /
  translationProfileId / customFormatProfileId / chapterCount /
  chapterProgress / scanlationGroups / translatedLanguages / metadataSource /
  contentRating / added / year / path / sizeOnDisk / genres / ratings /
  certification / tags / actions`). LocalStorage key: `manga_options`. Note:
  the `originalCountry` and `originalLanguage` column registrations survive
  in the columns array for persisted-state back-compat — the corresponding
  fields were removed from the `Manga` interface in Plan 17.3-12 D-13 so
  these columns render em-dash placeholders.
- **Cover-art URL** read off `manga.images[].url` per Lock #3 — already
  rewritten by `MangaController.MapResource → _coverMapper.ConvertToLocalUrls`
  to `/MediaCover/manga/{id}/poster.jpg`.
- **SignalR cache key:** `['/manga']` (Plan 07-02 contract). Backend
  `manga` resource pushes invalidate the manga list within 1s.

## Sonarr Inheritance — historical (Phase 17.3 retired)

At Phase 7 (Plan 07-04), several Manga/Index/ components delegated to
Sonarr `frontend/src/Series/` peers (`Series/Delete/DeleteSeriesModal`,
`Series/Edit/EditSeriesModal`, `Utilities/Series/getProgressBarKind`,
`Series/SeriesStatus.getSeriesStatusDetails`) and widened helper signatures
to accept manga-domain inputs. Phase 17.3 retired this pattern in two
steps:

| Phase 17.3 step | What changed |
|-----------------|--------------|
| Plan 17.3-08 (D-14) | Per-component forks: `MangaIndexRow.tsx`, `MangaIndexOverviewInfo.tsx`, `MangaIndexPoster.tsx`, `EditMangaModalContent.tsx` each absorbed the helper logic they previously imported from `Series/` / `Utilities/Series/`; CSS classes for dropped columns deleted in companion `.css` files. |
| Plan 17.3-13 (D-09/D-10) | Atomic stub-dir delete: `frontend/src/Series/` + `frontend/src/Episode/` + `frontend/src/EpisodeFile/` + `frontend/src/Season/` + `frontend/src/Utilities/Series/` all deleted in one commit after `tsc --noEmit` returned 0. |

Single-manga Edit + Delete modals had already shipped as dedicated peers
under `Manga/Edit/` (PR #27 — `fix(manga-edit-button-no-op)`) and
`Manga/Delete/` (`fix(manga-delete-button-no-op)`) before the stub-dir
delete. The widened helper signatures on `getProgressBarKind` /
`getSeriesStatusDetails` / `getIndexOfFirstCharacter` no longer survive
because the helper functions are now inlined into the per-component
forks.

## Cross-References

- [Manga/CLAUDE.md](../CLAUDE.md) — peer files (useManga, NoManga, MangaPoster, …)
- [.planning/phases/07-api-v5-frontend-manga-shell/07-RESEARCH.md](../../../../.planning/phases/07-api-v5-frontend-manga-shell/07-RESEARCH.md) — Lock #2, Lock #3, Lock #10
- [.planning/phases/07-api-v5-frontend-manga-shell/07-04-PLAN.md](../../../../.planning/phases/07-api-v5-frontend-manga-shell/07-04-PLAN.md)
- [.planning/phases/17.3-domain-rename-residue-sweep-pre-v1-sweep-tv-term-residue-pha/17.3-08-PLAN.md](../../../../.planning/phases/17.3-domain-rename-residue-sweep-pre-v1-sweep-tv-term-residue-pha/17.3-08-PLAN.md) — D-14 per-component fork
- [.planning/phases/17.3-domain-rename-residue-sweep-pre-v1-sweep-tv-term-residue-pha/17.3-13-PLAN.md](../../../../.planning/phases/17.3-domain-rename-residue-sweep-pre-v1-sweep-tv-term-residue-pha/17.3-13-PLAN.md) — D-09/D-10 atomic stub-dir delete
