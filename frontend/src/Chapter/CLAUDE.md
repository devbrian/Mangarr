# Chapter/

## Purpose

Chapter-domain TypeScript type, hooks, and utility cell/badge/modal components.
Originally a parallel sibling of `frontend/src/Episode/`; Phase 17.3 Plan 17.3-13
atomic stub-dir delete (D-09/D-10) retired the `Episode/` peer. This directory
is the canonical home for chapter frontend types + hooks + components. Mirrors
the V5 backend `ChapterResource` (Phase 7 Plan 07-01) and consumes the
URL-shaped React Query cache contract from Plan 07-02 SignalR handlers.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\src\Chapter\`

## Key Files

| File | Purpose |
|------|---------|
| `Chapter.ts` | `Chapter` interface (extends `ModelBase`) + `ChapterType` literal (`'Regular' \| 'Special' \| 'Oneshot' \| 'Extra'`). Mirrors `ChapterResource.cs` field-for-field. **(Plan 07-03)** |
| `useChapter.ts` | React Query hooks: `useChaptersByManga(mangaId)` (GET `/api/v5/chapter?mangaId=`), `useSingleChapter(id)` (GET `/api/v5/chapter/{id}`), `useToggleChapterMonitored(chapter)` (PUT `/api/v5/chapter/{id}` with full body), `useBulkToggleChaptersMonitored()` (PUT `/api/v5/chapter/monitor` with `{ ChapterIds, Monitored }` body). All bound to the Plan 07-02 SignalR cache key contract: list reads land at `['/chapter']` / `['/chapter', { mangaId }]`. **(Plan 07-05)** |
| `ChapterStatus.tsx` | 6-state status icon — Lock #4 set: `failed > blocklisted > have-file > queued > wanted > unmonitored` (precedence). Reads from `chapter.chapterFileId` + the Plan 07-02 SignalR caches `['/manga/queue']` / `['/manga/blocklist']` for queue/blocklist state, AND from `MangaChapterHistoryContext` (issue #51 fix — parent-fetched whole-manga history bucketed by chapterId via `MangaDetailsProvider`) for the `failed` state. SignalR-pushed updates re-render this within ~1 s of backend events. **NEVER fires its own `chapterId`-filtered `/manga/history` query** — restoring per-row history fetching would re-introduce issue #51's N+1 fan-out. **(Plan 07-05; issue #51 follow-up 2026-05-10)** |
| `LanguageBadge.tsx` + `.css` + `.css.d.ts` | Pill-shaped BCP-47 badge (UI-SPEC §Translation language badge). Background flips to `themeBlue` accent when chapter language matches the user's #1-ranked language on the default Translation Profile (Phase 5 D-01). Phase 11 rebrands `themeBlue` from Sonarr cyan to manga pink (`#f06292`); accent flip lands automatically. **(Plan 07-05)** |
| `ChapterNumber.tsx` | Decimal-aware chapter-number formatter — integer-shorthand for whole numbers; trimmed-decimal for fractional values (`1.5` not `1.500`). Optional `volumeNumber` rendered as `Vol. N ` prefix when `showVolumeNumber={true}`. Volumes are display-only — no Volumes table per PROJECT.md "Volumes/Seasons" Out-of-Scope. **(Plan 07-05)** |
| `ChapterTitleLink.tsx` | `<Link>` wrapper that opens `ChapterDetailsModal` on press. **(Plan 07-05)** |
| `ChapterDetailsModal.tsx` | Modal wrapper for the chapter detail view — v1 ships search-first (always lands on the InteractiveSearch panel; per-chapter Details / History tabs deferred to a future plan). Body renders `<InteractiveSearch searchPayload={{ kind: 'chapter', chapterId }} />` per Plan 07-05 Lock #14 (the `type` prop was retired in issue #263 — `searchPayload.kind` is the sole discriminator). **(Plan 07-05)** |
| `ChapterSearchCell.tsx` | Per-row search affordance pair (UI-04 + Open Question 3 lean): an Auto Search button (POST `/api/v5/chapter/{id}/search` — Plan 07-01 endpoint, enqueues `ChapterSearchCommand`) AND an Interactive Search button (opens `ChapterDetailsModal`). Mounted by `Manga/Details/ChapterRow.tsx` in the `actions` column. **(Plan 07-05)** |

## Patterns / Conventions

- **Field set is exact-mirror of `ChapterResource.cs`** — do NOT add fields the
  backend does not emit. `lastSearchTime`, `grabDate`, `runtime`, `airDate*`,
  `seasonNumber`, `episodeNumber`, `scene*`, `tvdbId` all explicitly excluded
  (drift markers — see `Chapter.ts` header comment for the rationale).
- **`chapterNumber: number`** — DECIMAL(10,3) per Phase 2 D-12 widen on the
  backend; JS `number` is precise enough for the supported decimal range
  (`1.0`, `1.5`, `1.123`, etc.).
- **`volumeNumber?: number`** — DISPLAY-ONLY per PROJECT.md "Volumes/Seasons"
  Out-of-Scope. There is NO Volumes table; this field is read from the source
  metadata for UX display only and never grouped on.
- **`isSynthetic: boolean`** — D-04 IsSynthetic-treated-identically pattern
  (Phase 6). Synthetic chapters (placeholders for missing absolute numbers)
  are search/Wanted/UI-treated like real chapters; do NOT filter on
  `!isSynthetic` anywhere.
- **`translatedLanguage`** is BCP-47; sentinel `'und'` for synthetic chapters.
- **`hasFile`** is computed on the backend (`HasFile => ChapterFileId.HasValue`)
  and emitted as a discrete bool field on the wire.
- **React Query cache key contract** (Plan 07-02): list reads use `['/chapter']`
  or `['/chapter', { mangaId }]`; single reads use `['/chapter/{id}']`. Plan
  07-02's `chapter` SignalR handler invalidates `['/chapter']` on every
  backend `ChapterUpdatedEvent`.
- **Status-precedence order** in `ChapterStatus.tsx`:
  `failed > blocklisted > have-file > queued > wanted > unmonitored`
  (Lock #4). Wrong precedence breaks the UI-04 chapter-row icon meaning.
- **No per-row `chapterId`-filtered queries inside `ChapterStatus`** — issue #51
  lifted the `lastEvent === 'downloadFailed'` lookup into
  `Manga/Details/MangaDetailsProvider` (one whole-manga `/manga/history?mangaIds=<id>`
  fetch, bucketed by chapterId, exposed via `useLastChapterHistoryEvent` from
  `Manga/Details/MangaChapterHistoryContext`). Sonarr-canonical mirror: matches
  `Activity/Queue/Details/QueueDetailsProvider` → `useQueueItemForEpisode`
  parent-provider pattern in upstream `Episode/EpisodeStatus.tsx`. Restoring a
  `useApiQuery({ path: '/manga/history', queryParams: { chapterId } })` call
  inside `ChapterStatus` would re-introduce N HTTP requests per chapter row.
- **Sibling-divergence comments (Pattern S2)** — every `.tsx` / `.ts` / `.css`
  file in this directory carries a `// Sonarr divergence: ...` header naming
  the role-match analog and the Phase 8 cleanup target.

## Manga Adaptation Notes

This directory IS the canonical chapter module. Phase 17.3 Plan 17.3-13
(D-09/D-10) atomic stub-dir delete completed the `Episode/` retirement.
The `// Phase 8 cleanup: collapse with X when Tv/ deletes.` markers in
this directory were resolved by Plan 17.3-13 (the stub dirs no longer
exist).

The `ChapterDetailsModal` is intentionally streamlined relative to the Sonarr
3-tab `EpisodeDetailsModal` (Details / History / Search). v1 ships search-first
because the Manga/Details Files / History tabs (Plans 07-08+) absorb the
per-tab content that would otherwise live in the modal. The streamlined shape
is the canonical Mangarr default.

## Cross-References

- [../Manga/CLAUDE.md](../Manga/CLAUDE.md) — Sibling manga type.
- [../Manga/Details/CLAUDE.md](../Manga/Details/CLAUDE.md) — Manga details page (Plan 07-05) — primary consumer of ChapterRow + ChapterStatus + ChapterSearchCell + LanguageBadge + ChapterNumber. Hosts `MangaDetailsProvider` + `MangaChapterHistoryContext` (issue #51 N+1 fix).
- [../InteractiveSearch/CLAUDE.md](../InteractiveSearch/CLAUDE.md) — `InteractiveSearch` component invoked by ChapterDetailsModal + ChapterSearchCell with `searchPayload={{ kind: 'chapter', chapterId }}`.
- [../../../src/Mangarr.Api.V5/Manga/Chapter/ChapterResource.cs](../../../src/Mangarr.Api.V5/Manga/Chapter/ChapterResource.cs) — Backend resource shape this type mirrors (Phase 7 Plan 07-01).
- [../../../src/Mangarr.Api.V5/Manga/Chapter/CLAUDE.md](../../../src/Mangarr.Api.V5/Manga/Chapter/CLAUDE.md) — Backend Chapter API surface.
- [../../../.planning/phases/07-api-v5-frontend-manga-shell/07-RESEARCH.md](../../../.planning/phases/07-api-v5-frontend-manga-shell/07-RESEARCH.md) — Lock #4 (6-state status badge sources), Lock #14 (InteractiveSearch payload extension).
- [../../../.planning/phases/07-api-v5-frontend-manga-shell/07-UI-SPEC.md](../../../.planning/phases/07-api-v5-frontend-manga-shell/07-UI-SPEC.md) — §Chapter status badge set; §Translation language badge.
