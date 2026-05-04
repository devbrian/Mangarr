# Chapter/

## Purpose

TypeScript type for the manga `Chapter` domain entity — parallel sibling of
`frontend/src/Episode/`. Mirrors the V5 backend `ChapterResource`
(`src/Sonarr.Api.V5/Manga/Chapter/ChapterResource.cs`, Phase 7 Plan 07-01).

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\src\Chapter\`

## Key Files

| File | Purpose |
|------|---------|
| `Chapter.ts` | `Chapter` interface (extends `ModelBase`) + `ChapterType` literal (`'Regular' \| 'Special' \| 'Oneshot' \| 'Extra'`). Mirrors `ChapterResource.cs` field-for-field. |

## Patterns / Conventions

- **Field set is exact-mirror of `ChapterResource.cs`** — do NOT add fields the
  backend does not emit. `lastSearchTime`, `grabDate`, `runtime`, `airDate*`,
  `seasonNumber`, `episodeNumber`, `scene*`, `tvdbId` all explicitly excluded
  (drift markers — see file header comment for the rationale).
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

## Phase 7 Plan 05+ Sibling Components (NOT YET CREATED)

When Plan 07-05 / 07-06 ship, this directory will gain (mirroring `Episode/`):

| File (future) | Purpose |
|---------------|---------|
| `ChapterDetailsModal.tsx` | Modal for Chapter detail view (mirror `EpisodeDetailsModal`). |
| `ChapterSearchCell.tsx` | Per-row "manual search" button on the Chapters tab. |
| `ChapterStatus.tsx` | Status icon (have-file / wanted / unmonitored / queued / failed / blocklisted). |
| `ChapterNumber.tsx` | Display component for chapter number + volume label. |
| `ChapterTitleLink.tsx` | Link cell to a chapter's detail modal. |
| `LanguageBadge.tsx` | BCP-47 language pill. |

These are **deferred to Plans 07-05 / 07-06** — Plan 07-03 ships only the type.

## Manga Adaptation Notes

This directory IS the manga adaptation of `frontend/src/Episode/`. Phase 8 cleanup
will collapse the two when `Episode/` deletes.

## Cross-References

- [../Episode/CLAUDE.md](../Episode/CLAUDE.md) — Sibling Episode feature (Phase 8 cleanup target).
- [../Manga/CLAUDE.md](../Manga/CLAUDE.md) — Sibling manga type.
- [../../../src/Sonarr.Api.V5/Manga/Chapter/ChapterResource.cs](../../../src/Sonarr.Api.V5/Manga/Chapter/ChapterResource.cs) — Backend resource shape this type mirrors (Phase 7 Plan 07-01).
- [../../../src/Sonarr.Api.V5/Manga/Chapter/CLAUDE.md](../../../src/Sonarr.Api.V5/Manga/Chapter/CLAUDE.md) — Backend Chapter API surface.
