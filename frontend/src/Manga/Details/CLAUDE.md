# Manga/Details/

## Purpose

Manga detail page — the user's primary working surface (UI-04). Shows the
manga hero header (cover, title, monitor toggle, status, links, tags,
progress) and a tabbed body (Overview / Chapters / Files / History / Search).

Originally a parallel sibling of `frontend/src/Series/Details/`; Phase 17.3
Plan 17.3-13 atomic stub-dir delete (D-09/D-10) retired the Series/Details/
peer. This directory is the canonical home for the manga detail page.

The Chapters tab is **flat** — no season grouping (PROJECT.md "Volumes /
Seasons" Out-of-Scope, UI-SPEC §Anti-pattern 5).


## Key Files

| File | Purpose |
|------|---------|
| `MangaDetailsPage.tsx` | Page route component. Resolves `:titleSlug` → manga via `useManga()` and renders `<MangaDetails mangaId>` or `<NotFound>`. Mirrors `SeriesDetailsPage.tsx` verbatim with manga renames + redirects to `/manga` (not `/`) when the manga is deleted out from under the page. |
| `MangaDetails.tsx` | Hero card + tab navigation + tab body dispatch. Hero header: cover (138 px), title (weight 300), monitor toggle, metadata strip (path / size / TranslationProfile / status / links / tags / chapter progress). Tabs: Overview, Chapters, Files, History, Search. Edit + Delete modals are dedicated manga peers under `Manga/Edit/` (PR #27 — `fix(manga-edit-button-no-op)`) and `Manga/Delete/` (`fix(manga-delete-button-no-op)`); the Plan 07-04 `seriesId={mangaId}` Sonarr-modal bridge has been retired (Sonarr `Series/Edit/EditSeriesModal` + `Series/Delete/DeleteSeriesModal` peers deleted in Phase 17.3 Plan 17.3-13). |
| `MangaDetailsFiles.tsx` | Files tab body — reads `useChapterFilesByManga(mangaId)` (`/api/v5/ChapterFile?mangaId=<id>`, Phase 13 Plan 13-07) and renders the chapter-file list. Empty state: `kinds.INFO` Alert (`NoChapterFilesYet`). |
| `MangaDetailsHistory.tsx` | History tab body — `usePagedApiQuery<ChapterHistory>` against `/api/v5/manga/history?mangaIds={id}`. Analog: TV `useSeriesHistory.ts`. |
| `MangaDetails.css` + `.css.d.ts` | CSS Modules — verbatim port of `SeriesDetails.css` (cover 250 / 368, title weight 300 / 50 px, gap 35 px, etc.) plus tab-button styles for the inline RESEARCH-A4 fallback (no `Components/Tab/` in the codebase). |
| `MangaDetailsProvider.tsx` | Page-scoped context provider wrapping the entire `MangaDetails` body. Issue #51 (per-chapter `/manga/history?chapterId=X` N+1) fix: fires ONE `usePagedApiQuery` against `/api/v5/manga/history?mangaIds={id}&sortKey=date&sortDirection=descending&pageSize=250`, buckets the descending-by-date records by `chapterId` into `Map<number, ChapterHistory[]>`, and exposes them via `MangaChapterHistoryContext`. ChapterStatus consumes this context instead of firing its own per-row query. (Future plans wire chapter-file context + manga-queue-details context onto the same provider.) |
| `MangaChapterHistoryContext.ts` | Context + `useLastChapterHistoryEvent(chapterId)` hook returning the most-recent `ChapterHistory` record for a given chapter — issue #51 fix. Reads from the bucketed map populated by `MangaDetailsProvider`. Returns `undefined` when the manga has no history for that chapter or before the provider's first fetch resolves. |
| `MangaDetailsLinks.tsx` + `.css` + `.css.d.ts` | External-source link badges. MangaDex / AniList / MyAnimeList — replaces TVDB / TVMaze / IMDB / TMDB. Read from singular `mangaDexId` / `aniListId` / `malId` per Phase 2 02-CONTEXT (manga is 1:1 across sources in v1). |
| `MangaAlternateTitles.tsx` + `.css` + `.css.d.ts` | Verbatim port of `SeriesAlternateTitles` — `<ul>` with comment-suffix render shape. |
| `MangaProgressLabel.tsx` | `chapterFileCount / chapterCount` Label with kind-by-progress branching (success / warning / danger). v1 omits the queue-details overlay (Plan 07-09 wires it). |
| `MangaTags.tsx` | Verbatim port of `SeriesTags` — `useTagList()` lookup → Label list. Manga shares the Tag entity. |
| `MangaDetailsChapters.tsx` | **Flat sortable Chapters table** (no season grouping). Reads via `useChaptersByManga(mangaId)` (from `Chapter/useChapter`). `DEFAULT_COLUMNS`: monitored, chapterNumber, title, status, actions (5 — matches the Phase 16.1 Sonarr-canonical set). Empty state per UI-SPEC §Empty states. |
| `ChapterRow.tsx` + `.css` + `.css.d.ts` | Per-chapter row rendering the 5 columns: monitor toggle, ChapterNumber, ChapterTitleLink, ChapterStatus (6-state), and an actions cell = `ChapterSearchCell` (Auto + Interactive search). Mirror of `EpisodeRow.tsx` minus the per-language / scene-numbering / MediaInfo cells. Monitor toggle dispatches via `useToggleChapterMonitored` (from `Chapter/useChapter`, PUT `/api/v5/chapter/{id}`). |

## Season omission (no manga analog)

The Sonarr `Series/Details/` season files (`SeasonInfo`, `SeasonProgressLabel`,
`SeriesDetailsSeason`) have no manga peer here (PROJECT.md "Volumes / Seasons"
Out-of-Scope). `MangaChapterHistoryContext.ts` is net-new with no Series analog:
Sonarr's `EpisodeStatus` reads `episode.grabbed` off the backend model, while
manga `Chapter` carries no equivalent flag, so issue #51 buckets history via
the provider instead.

## Patterns / Conventions

- **Tab navigation is stateful inline** — RESEARCH Assumption A4 fallback
  because `frontend/src/Components/Tab/` does NOT exist. The active tab key
  is `useState<TabKey>('overview')`; switching is a sticky-header click
  handler. If a real Tab component is later added, the buttons can swap.
- **Chapters tab is FLAT** — UI-SPEC §Anti-pattern 5 + PROJECT.md "Volumes /
  Seasons" Out-of-Scope. Do NOT introduce season grouping here. The flat
  shape is locked.
- **Edit + Delete modals are dedicated manga siblings** under
  `Manga/Edit/` (PR #27 — `fix(manga-edit-button-no-op)`) and
  `Manga/Delete/` (`fix(manga-delete-button-no-op)`). Both replaced the
  Phase 15 Plan 15-12 stubs (`{isEditModalOpen ? null : null}` /
  `{isDeleteModalOpen ? null : null}`). Earlier `seriesId={mangaId}` bridge
  notes are obsolete.
- **Files / History tabs render real content** — `MangaDetailsFiles.tsx`
  (Plan 07-08, `useChapterFilesByManga`) and `MangaDetailsHistory.tsx`
  (Plan 07-09, `usePagedApiQuery<ChapterHistory>`). Empty states use
  `kinds.INFO` Alerts.
- **Search tab uses the extended InteractiveSearch payload union** — Plan
  07-05 Task 3 added `MangaSearchPayload` (`{ mangaId }`); the Search tab
  fires `<InteractiveSearch searchPayload={{ kind: 'manga', mangaId }} />`
  (whole-manga search). Per-chapter search lives on `ChapterSearchCell`
  inside the Chapters tab via `<InteractiveSearch searchPayload={{ kind: 'chapter', chapterId }} />`.
  (The `type` prop was retired in issue #263; `searchPayload.kind` is the sole discriminator.)
- **TranslationProfileName lookup is inlined** — until Plan 07 ships
  `Settings/Profiles/Translations/TranslationProfileName.tsx`, the Manga
  Details metadata strip resolves the profile name via a local `useApiQuery`
  against `/api/v5/translationprofile`.
- **ChapterStatus reads chapter-history via parent context — NEVER fires its
  own `chapterId`-filtered query.** Issue #51 lifted the per-row history
  fetch into `MangaDetailsProvider`. Restoring a `useApiQuery({ path:
  '/manga/history', queryParams: { chapterId } })` call inside `ChapterStatus`
  would re-introduce the N+1 fan-out (1 HTTP request per visible chapter row).
  Sonarr-canonical mirror: matches the `QueueDetailsProvider` →
  `useQueueItemForEpisode(episodeId)` parent-provider pattern in upstream
  `Series/Details/SeriesDetailsProvider.tsx` + `Episode/EpisodeStatus.tsx`.
- **Sibling-divergence comments (Pattern S2)** — every `.tsx` / `.ts` file
  here carries the `// Sonarr divergence: ...` header naming the role-match
  analog and the Phase 8 cleanup target.

## Manga Adaptation Notes

This directory IS the manga canonical for manga details. Phase 17.3 Plan
17.3-13 (D-09/D-10) atomic stub-dir delete completed the `Series/Details/`
retirement.

The 8 omitted Series files are season-related and have no manga analog
(PROJECT.md Volumes/Seasons Out-of-Scope). Three new files have NO Series
analog:
- `MangaDetailsChapters.tsx` — flat sortable Chapters tab body (Series uses
  collapsible season cards via `SeriesDetailsSeason.tsx`).
- The inline tab-navigation buttons in `MangaDetails.tsx` — Series renders
  a non-tabbed page.
- `MangaChapterHistoryContext.ts` — issue #51 N+1 fix; Sonarr `EpisodeStatus`
  doesn't need per-episode history because `Episode.grabbed` is backend-pushed
  on the Episode model itself (manga `Chapter` carries no equivalent flag).

## Cross-References

- Sonarr `Series/Details/` (the sibling this detail page was forked from) was deleted in Phase 17.3 Plan 17.3-13 atomic stub-dir delete — see `DIVERGENCE.md`. This `Manga/Details/` subtree is now canonical.
- [../../Chapter/CLAUDE.md](../../Chapter/CLAUDE.md) — Chapter utility components consumed by ChapterRow + MangaDetailsChapters.
- [../../InteractiveSearch/CLAUDE.md](../../InteractiveSearch/CLAUDE.md) — Search tab + ChapterDetailsModal consumer.
- [../CLAUDE.md](../CLAUDE.md) — Parent Manga directory.
- [../../../../.planning/phases/07-api-v5-frontend-manga-shell/07-05-PLAN.md](../../../../.planning/phases/07-api-v5-frontend-manga-shell/07-05-PLAN.md) — This plan.
- [../../../../.planning/phases/07-api-v5-frontend-manga-shell/07-UI-SPEC.md](../../../../.planning/phases/07-api-v5-frontend-manga-shell/07-UI-SPEC.md) — §Manga Detail (hero card + tabs spec); §Empty states (locked Chapters/Files/History tab copy); §Chapter status badge set; §Translation language badge.
- [../../../../.planning/phases/07-api-v5-frontend-manga-shell/07-RESEARCH.md](../../../../.planning/phases/07-api-v5-frontend-manga-shell/07-RESEARCH.md) — Lock #11 (23-file mirror; 8 omissions); Lock #14 (InteractiveSearch payload extension).
