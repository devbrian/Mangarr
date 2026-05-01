# Wanted/

## Purpose

Two views into "what's missing or upgradable":

- **Missing** — episodes that should exist (monitored + aired) but have no file
- **CutoffUnmet** — episodes that have a file, but the file is below the quality profile's cutoff (i.e., upgradable)

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\src\Wanted\`

## Subdirectories

### Missing/ (~6 files)
| File | Purpose |
|------|---------|
| `Missing.tsx` | Page (server-paged table) |
| `MissingRow.tsx` | One missing episode row |
| `MissingFilterModal.tsx` | Filter |
| `missingOptionsStore.ts` | Zustand options |
| `useMissing.tsx` | API hook |

### CutoffUnmet/ (~5 files)
| File | Purpose |
|------|---------|
| `CutoffUnmet.tsx` | Page |
| `CutoffUnmetRow.tsx` | Row |
| `cutoffUnmetOptionsStore.ts` | Zustand |
| `useCutoffUnmet.tsx` | Hook |

Note: A `CutoffUnmetFilterModal` is referenced via the missing one (or there's a shared filter component). Both pages share table column definitions.

## API

- `GET /api/v5/wanted/missing` → server-paged list
- `GET /api/v5/wanted/cutoff` → server-paged list

Per-row actions:
- "Search" → executes `EpisodeSearch` command
- "Toggle Monitor" → PUT `/api/v5/episode/{id}` with `{monitored: false}`

Bulk actions (footer):
- Search all
- Toggle monitor
- Remove (set unmonitored — not delete)

## Manga Adaptation Notes

These pages are **conceptually identical** for manga: just rename the data type.

- `Missing` → "missing chapters" — chapters that should exist but no file
- `CutoffUnmet` → "below cutoff chapters" — files exist but quality profile isn't satisfied

### Key Considerations
- Manga has chapter scheduling that's less precise than TV airing — backend logic for "should exist by now" needs adaptation:
  - Sonarr: airDateUtc < now → missing
  - Mangarr: lastKnownChapterReleaseDate < now → missing? Or just any monitored chapter without a file?
- "Cutoff" semantics carry over (high-res scan vs. low-res; official vs. fan; etc.)

### File Renames
| Sonarr | Manga |
|--------|-------|
| `MissingRow.tsx` (Episode columns) | Update to Chapter columns |
| `useMissing.tsx` (returns Episode rows) | `useMissingChapters.tsx` |

## Cross-References

- [../../CLAUDE.md](../../CLAUDE.md) — Frontend overview
- [../Episode/CLAUDE.md](../Episode/CLAUDE.md) — Episode types used
- [../Series/CLAUDE.md](../Series/CLAUDE.md) — Each row links back to series
- [../../../src/NzbDrone.Core/DecisionEngine/CLAUDE.md](../../../src/NzbDrone.Core/DecisionEngine/CLAUDE.md) — Cutoff logic lives in `CutoffSpecification`
