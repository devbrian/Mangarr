# ChapterFile/

## Purpose

ChapterFile-domain TypeScript type, React Query hooks, and presentational row
component. Authored 2026-05-12 per **issue #84** (Plan 17.3-16 deferral
resolution — Option A): formal frontend peer directory that mirrors the
backend `src/NzbDrone.Core/MediaFiles/ChapterFile.cs` entity 1:1, restoring
the Sonarr-shape parallelism between backend entities and frontend peer dirs.

Mirrors the V5 backend `ChapterFileResource` (Phase 13 Plan 13-07) and
consumes the URL-shaped React Query cache contract from Plan 07-02 SignalR
handlers (`chapterfile` resource name → `['/chapterFile']` cache key).


## Key Files

| File | Purpose |
|------|---------|
| `ChapterFile.ts` | `ChapterFile` interface (extends `ModelBase`). Mirrors `ChapterFileResource.cs` field-for-field: `mangaId`, `chapterId`, `relativePath?`, `path?`, `size`, `dateAdded`, `translatedLanguage?` (BCP-47), `scanlationGroup?`. **(issue #84)** |
| `useChapterFile.ts` | React Query hooks: `useChapterFilesByManga(mangaId)` (GET `/api/v5/ChapterFile?mangaId=`), `useDeleteChapterFile(id)` (DELETE `/api/v5/ChapterFile/{id}`), `useDeleteChapterFiles()` (DELETE `/api/v5/ChapterFile/bulk` with body `{ chapterFileIds }`). All bound to the Plan 07-02 SignalR cache key contract: list reads land at `['/chapterFile']`. **(issue #84)** |
| `ChapterFileRow.tsx` (+ `.css` / `.css.d.ts`) | Row for the Manga Details > Files tab table. Renders path, size, translated-language badge (delegated to `Chapter/LanguageBadge` — accent flip when the file's language matches the user's #1-ranked language on the default Translation Profile), scanlation group, date added, and an **actions cell with a delete `IconButton`** (`icons.DELETE`). The row owns the delete mutation (`useDeleteChapterFile(id, blocklist)`) + the blocklist-checkbox state and opens `ChapterFileDeleteModal`. **(issue #84; delete affordance 2026-06-20)** |
| `ChapterFileDeleteModal.tsx` | Presentational confirm modal for the per-row delete — file-name confirmation message + a "Blocklist Release" checkbox (`?blocklist=true`). Shape mirrors `Activity/Queue/RemoveQueueItemModal.tsx`. No `EpisodeFile/` Sonarr peer — manga-only affordance. **(2026-06-20)** |

## Patterns / Conventions

- **Field set is exact-mirror of `ChapterFileResource.cs`** — do NOT add fields
  the backend does not emit. TV-only fields (`seasonNumber`, `sceneName`,
  `releaseGroup`, `quality`, `customFormats`, `customFormatScore`, `indexerFlags`,
  `releaseType`, `mediaInfo`, `qualityCutoffNotMet`) are explicitly excluded
  per Phase 13 Plan 13-07 ChapterFileResource design (see header comments
  on `ChapterFile.ts` for the per-field rationale).
- **`translatedLanguage`** is BCP-47 single string per Phase 16.1 D-04
  group-axis collapse (NOT a `Language[]` like Sonarr's `EpisodeFile.languages`).
- **`scanlationGroup`** is the canonical "release group" axis for manga
  per Phase 16.1 D-04/D-05/D-06 — the wire field name is `scanlationGroup`,
  NOT `releaseGroup`.
- **React Query cache key contract** (Plan 07-02): list reads use
  `['/chapterFile']` or `['/chapterFile', { mangaId }]`. The `chapterfile`
  SignalR handler in `Components/SignalRListener.tsx` invalidates the same
  key on `ChapterFileAddedEvent` / `ChapterFileDeletedEvent` backend fan-out.
- **No `useUpdateChapterFiles` hook** — the backend `ChapterFileController`
  carries NO PUT endpoint (Plan 13-07 design — manga has no Quality field,
  and per-file ScanlationGroup edits arrive via the editor flow rather than
  the file controller). Add an update hook only when a matching backend PUT
  lands.
- **`LanguageBadge` reuse**: `ChapterFileRow` delegates the translated-language
  cell to `Chapter/LanguageBadge` — same accent semantics + same pill styling
  as the Chapter row. Do NOT duplicate the badge component inside this peer
  dir (single canonical home in `Chapter/`).
- **Sibling-divergence comments (Pattern S2)** — every `.tsx` / `.ts` file in
  this directory carries a `// Sonarr divergence: ...` header naming the
  role-match analog (`v5-develop` Sonarr branch) and the rationale for any
  shape deviations.

## Manga Adaptation Notes

This directory IS the canonical chapter-file frontend module. Phase 17.3
Plan 17.3-13b atomically deleted the stub `frontend/src/EpisodeFile/` peer
(4 no-op stub files) because the stubs rendered nothing user-visible at
the time. Issue #84 reverses that decision for ChapterFile only — Option A
restores the peer-dir parity because the manga-domain ChapterFile entity
HAS user-visible call sites (the Manga Details > Files tab) and the inline
interface + cell composition that previously lived in
`Manga/Details/MangaDetailsFiles.tsx` (lines 28-38 / 168-180 of the
pre-issue-#84 file) is now extracted into this peer dir.

The GET-by-mangaId + single-delete hooks ship load-bearing on day one via
`MangaDetailsFiles`. The **per-row single-delete affordance shipped 2026-06-20**
(`ChapterFileRow` delete button → `ChapterFileDeleteModal` confirm with a
"Blocklist Release" checkbox; `useDeleteChapterFile(id, blocklist)` →
`DELETE /api/v5/ChapterFile/{id}?blocklist=true`; the backend blocklists the
release from the chapter's most-recent Grabbed history row — see
`DIVERGENCE.md` "Blocklist-on-delete affordance"). Still-future ChapterFile-specific
UI (bulk-delete affordance on the Files tab; per-row metadata-edit modal; the
InteractiveImport ChapterFile selection flow that Plan 17.3-13b stubbed
inline at `InteractiveImport/Interactive/InteractiveImportModalContent.tsx:285,601`)
should consume this peer dir rather than re-introducing inline shapes.

## Cross-References

- [../Chapter/CLAUDE.md](../Chapter/CLAUDE.md) — Sibling chapter type/hook/components peer dir; `LanguageBadge` is consumed from there.
- [../Manga/Details/MangaDetailsFiles.tsx](../Manga/Details/MangaDetailsFiles.tsx) — Primary consumer (Files tab on the Manga details page).
- [../../../src/Mangarr.Api.V5/Manga/Chapter/ChapterFileResource.cs](../../../src/Mangarr.Api.V5/Manga/Chapter/ChapterFileResource.cs) — Backend resource shape this type mirrors (Phase 13 Plan 13-07).
- [../../../src/Mangarr.Api.V5/Manga/Chapter/ChapterFileController.cs](../../../src/Mangarr.Api.V5/Manga/Chapter/ChapterFileController.cs) — Backend ChapterFile API surface (GET / DELETE / DELETE bulk + SignalR fan-out).
- [../../../src/NzbDrone.Core/MediaFiles/ChapterFile.cs](../../../src/NzbDrone.Core/MediaFiles/ChapterFile.cs) — Backend model with the canonical field set this peer mirrors.
- [../Components/SignalRListener.tsx](../Components/SignalRListener.tsx) — `chapterfile` handler (Plan 13-07) — invalidates `['/chapterFile']` on backend fan-out.
- [../../../.planning/debug/resolved/issue-84-chapterfile-frontend.md](../../../.planning/debug/resolved/issue-84-chapterfile-frontend.md) — Authoring debug session.
