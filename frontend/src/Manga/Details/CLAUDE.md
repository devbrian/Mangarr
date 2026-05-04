# Manga/Details/

## Purpose

Manga detail page — the user's primary working surface (UI-04). Shows the
manga hero header (cover, title, monitor toggle, status, links, tags,
progress) and a tabbed body (Overview / Chapters / Files / History / Search).
Parallel sibling of `frontend/src/Series/Details/`.

The Chapters tab is **flat** — no season grouping (PROJECT.md "Volumes /
Seasons" Out-of-Scope, UI-SPEC §Anti-pattern 5).

**Absolute Path:** `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\src\Manga\Details\`

## Key Files

| File | Purpose |
|------|---------|
| `MangaDetailsPage.tsx` | Page route component. Resolves `:titleSlug` → manga via `useManga()` and renders `<MangaDetails mangaId>` or `<NotFound>`. Mirrors `SeriesDetailsPage.tsx` verbatim with manga renames + redirects to `/manga` (not `/`) when the manga is deleted out from under the page. |
| `MangaDetails.tsx` | Hero card + tab navigation + tab body dispatch. Hero header: cover (138 px), title (weight 300), monitor toggle, metadata strip (path / size / TranslationProfile / status / links / tags / chapter progress). Tabs: Overview, Chapters, Files (placeholder), History (placeholder — Plan 07-09), Search. Edit + Delete modals reuse `Series/Edit/EditSeriesModal` + `Series/Delete/DeleteSeriesModal` via `seriesId=mangaId` (Plan 07-04 precedent — Phase 8 forks dedicated manga modals). |
| `MangaDetails.css` + `.css.d.ts` | CSS Modules — verbatim port of `SeriesDetails.css` (cover 250 / 368, title weight 300 / 50 px, gap 35 px, etc.) plus tab-button styles for the inline RESEARCH-A4 fallback (no `Components/Tab/` in the codebase). |
| `MangaDetailsProvider.tsx` | Context provider passthrough. v1 ships as a passthrough; future plans will wire chapter-file context + manga-queue-details context here without forcing a render-tree refactor on consumers. |
| `MangaDetailsLinks.tsx` + `.css` + `.css.d.ts` | External-source link badges. MangaDex / AniList / MyAnimeList — replaces TVDB / TVMaze / IMDB / TMDB. Read from singular `mangaDexId` / `aniListId` / `malId` per Phase 2 02-CONTEXT (manga is 1:1 across sources in v1). |
| `MangaAlternateTitles.tsx` + `.css` + `.css.d.ts` | Verbatim port of `SeriesAlternateTitles` — `<ul>` with comment-suffix render shape. |
| `MangaProgressLabel.tsx` | `chapterFileCount / chapterCount` Label with kind-by-progress branching (success / warning / danger). v1 omits the queue-details overlay (Plan 07-09 wires it). |
| `MangaTags.tsx` | Verbatim port of `SeriesTags` — `useTagList()` lookup → Label list. Manga shares the Tag entity. |
| `MangaDetailsChapters.tsx` | **Flat sortable Chapters table** (no season grouping). Reads via `useChaptersByManga(mangaId)` (`['/chapter', { mangaId }]`). Columns: monitored, chapterNumber, title, translatedLanguage, scanlationGroup, releaseDate, status, actions. Empty state per UI-SPEC §Empty states. Closest analog is `Wanted/Missing/Missing.tsx` (PATTERNS "No Analog Found"). |
| `ChapterRow.tsx` + `.css` + `.css.d.ts` | Per-chapter row with monitor toggle, ChapterNumber cell, ChapterTitleLink cell, LanguageBadge cell, ScanlationGroup cell, RelativeDateCell, ChapterStatus cell (6-state), ChapterSearchCell (Auto + Interactive search). Mirror of `EpisodeRow.tsx` minus scene-numbering / EpisodeFileLanguages / MediaInfo / IndexerFlags / runtime / finaleType cells. Monitor toggle dispatches via `useToggleChapterMonitored` (PUT `/api/v5/chapter/{id}` — Plan 07-01 endpoint). |

## File-count

Plan 07-05 ships **17 files** under this directory (16 `.tsx`/`.css`/`.css.d.ts`
+ this CLAUDE.md). Source `Series/Details/` ships 23 files; the 8 omitted
files (Lock #11) are:
- `SeasonInfo.{tsx,css,css.d.ts}`
- `SeasonProgressLabel.tsx`
- `SeriesDetailsSeason.{tsx,css,css.d.ts}`

These season-related files have no manga analog (PROJECT.md "Volumes /
Seasons" Out-of-Scope).

## Patterns / Conventions

- **Tab navigation is stateful inline** — RESEARCH Assumption A4 fallback
  because `frontend/src/Components/Tab/` does NOT exist. The active tab key
  is `useState<TabKey>('overview')`; switching is a sticky-header click
  handler. If a real Tab component is later added, the buttons can swap.
- **Chapters tab is FLAT** — UI-SPEC §Anti-pattern 5 + PROJECT.md "Volumes /
  Seasons" Out-of-Scope. Do NOT introduce season grouping here. The flat
  shape is locked.
- **Edit + Delete modals reuse Sonarr Series modals** (`Series/Edit/EditSeriesModal`
  + `Series/Delete/DeleteSeriesModal`) via `seriesId={mangaId}` — Plan 07-04
  documented this for the Index page; Plan 07-05 inherits the convention.
  Phase 8 forks dedicated manga modals.
- **Files / History tabs are v1 placeholders** — Plan 07-08 (Files tab) and
  Plan 07-09 (History wrapper) wire the real content. v1 placeholders use
  `kinds.INFO` Alerts so users see the surface without faking data.
- **Search tab uses the extended InteractiveSearch payload union** — Plan
  07-05 Task 3 added `MangaSearchPayload` (`{ mangaId }`); the Search tab
  fires `<InteractiveSearch type="manga" searchPayload={{ mangaId }} />`
  (whole-manga search). Per-chapter search lives on `ChapterSearchCell`
  inside the Chapters tab via `<InteractiveSearch type="chapter" />`.
- **TranslationProfileName lookup is inlined** — until Plan 07 ships
  `Settings/Profiles/Translations/TranslationProfileName.tsx`, the Manga
  Details metadata strip resolves the profile name via a local `useApiQuery`
  against `/api/v5/translationprofile`.
- **Sibling-divergence comments (Pattern S2)** — every `.tsx` / `.ts` file
  here carries the `// Sonarr divergence: ...` header naming the role-match
  analog and the Phase 8 cleanup target.

## Manga Adaptation Notes

This directory IS the manga adaptation of `frontend/src/Series/Details/`.
Phase 8 cleanup will collapse the two when `Series/Details/` deletes.

The 8 omitted Series files are season-related and have no manga analog
(PROJECT.md Volumes/Seasons Out-of-Scope). Two new files have NO Series
analog:
- `MangaDetailsChapters.tsx` — flat sortable Chapters tab body (Series uses
  collapsible season cards via `SeriesDetailsSeason.tsx`).
- The inline tab-navigation buttons in `MangaDetails.tsx` — Series renders
  a non-tabbed page.

## Cross-References

- [../../Series/Details/CLAUDE.md](../../Series/Details/CLAUDE.md) — Sibling Series detail page (Phase 8 cleanup target).
- [../../Chapter/CLAUDE.md](../../Chapter/CLAUDE.md) — Chapter utility components consumed by ChapterRow + MangaDetailsChapters.
- [../../InteractiveSearch/CLAUDE.md](../../InteractiveSearch/CLAUDE.md) — Search tab + ChapterDetailsModal consumer.
- [../CLAUDE.md](../CLAUDE.md) — Parent Manga directory.
- [../../../../.planning/phases/07-api-v5-frontend-manga-shell/07-05-PLAN.md](../../../../.planning/phases/07-api-v5-frontend-manga-shell/07-05-PLAN.md) — This plan.
- [../../../../.planning/phases/07-api-v5-frontend-manga-shell/07-UI-SPEC.md](../../../../.planning/phases/07-api-v5-frontend-manga-shell/07-UI-SPEC.md) — §Manga Detail (hero card + tabs spec); §Empty states (locked Chapters/Files/History tab copy); §Chapter status badge set; §Translation language badge.
- [../../../../.planning/phases/07-api-v5-frontend-manga-shell/07-RESEARCH.md](../../../../.planning/phases/07-api-v5-frontend-manga-shell/07-RESEARCH.md) — Lock #11 (23-file mirror; 8 omissions); Lock #14 (InteractiveSearch payload extension).
