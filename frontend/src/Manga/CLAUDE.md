# Manga/

## Purpose

TypeScript types + zustand options store for the manga domain — the parallel sibling
of `frontend/src/Series/`. Forward-looking shape that downstream Phase 7 React plans
(07-04 Manga Index pages, 07-05 Manga Details, 07-06 Chapter tab, 07-09/07-10
Activity / Wanted) consume.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\src\Manga\`

## Key Files

| File | Purpose |
|------|---------|
| `Manga.ts` | `Manga` interface (extends `ModelBase`) + `MangaMonitor` 5-value type literal (per Phase 6 D-03 — `'all' \| 'future' \| 'missing' \| 'latest' \| 'none'`) + `MangaStatus` + `MangaImage`. Mirrors the V5 backend `MangaResource` (`src/Sonarr.Api.V5/Manga/MangaResource.cs`). |
| `MangaStatus.ts` | `MANGA_STATUS_VALUES` constant array + `isMonitorableStatus()` helper. Sibling of `Series/SeriesStatus.ts` (which is utility helpers, not an enum file). |
| `mangaOptionsStore.ts` | Zustand store backed by `localStorage` key **`'manga_options'`**. 3-view toggle (`posters` / `overview` / `table`), default `view: 'posters'` per Lock #2. Exports `useMangaOptions` / `setMangaOptions` / `useMangaOption` / `setMangaOption` / `setMangaSort` / `getMangaOptions`. The `columns` array is intentionally empty in this plan — **Plan 07-04 fills the canonical column list**. |

## Patterns / Conventions

- **localStorage key:** `manga_options` (NOT `series_options`). See verification grep
  in `07-03-SUMMARY.md` confirming no collision.
- **5-value `MangaMonitor`** — Phase 6 D-03 is locked at `'all' | 'future' | 'missing' | 'latest' | 'none'`. Do NOT extend this without a documented decision.
- **Default view = `'posters'`** — Lock #2 from `07-RESEARCH.md`. Manga covers
  communicate the title visually; same as Sonarr's Series default.
- **Column array is a Plan-04 stub.** Plan 07-03 ships an empty `columns: []` so that
  TypeScript compiles. Plan 07-04 fills the canonical column list (status, sortTitle,
  translationProfile, customFormatProfile, chapterCount, chapterProgress, etc.) per
  the Sonarr `seriesOptionsStore.ts` pattern.
- **Sibling-divergence comments (Pattern S2)** — every file here carries the
  `// Sonarr divergence: NEW manga sibling per Phase 7 D-NN — see DIVERGENCE.md.` header.

## Manga Adaptation Notes

This directory IS the manga adaptation of `frontend/src/Series/`. Phase 8 cutover
will collapse the two when `Series/` deletes — at which point all the
`// Phase 8 cleanup: collapse with Series when Tv/ deletes.` markers in this
directory point Phase 8's executor at the canonical sibling to merge.

## Cross-References

- [../Series/CLAUDE.md](../Series/CLAUDE.md) — Sibling Series feature (Phase 8 cleanup target).
- [../Chapter/CLAUDE.md](../Chapter/CLAUDE.md) — Chapter sibling type.
- [../AddManga/CLAUDE.md](../AddManga/CLAUDE.md) — Add-manga flow + form-state store.
- [../typings/CLAUDE.md](../typings/CLAUDE.md) — Cross-cutting typing folder (`MangaQueueItem.ts`, `ChapterHistory.ts`, `MangaBlocklist.ts`).
- [../Helpers/Hooks/useOptionsStore.ts](../Helpers/Hooks/useOptionsStore.ts) — `createOptionsStore<T>(name, state, options?)` factory.
- [../../../src/Sonarr.Api.V5/Manga/MangaResource.cs](../../../src/Sonarr.Api.V5/Manga/MangaResource.cs) — Backend resource shape this type mirrors.
- [../../../.planning/phases/07-api-v5-frontend-manga-shell/07-RESEARCH.md](../../../.planning/phases/07-api-v5-frontend-manga-shell/07-RESEARCH.md) — Lock #2 (default view), Example 4 (addMangaOptionsStore pattern).
