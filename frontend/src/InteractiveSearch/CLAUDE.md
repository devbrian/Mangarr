# InteractiveSearch/

## Purpose

**Manual release search** — user clicks "Search" on a series/season/episode, sees all releases the configured indexers return, and manually picks one to download. Bypasses the automated DecisionEngine ranking (though rejection reasons are still shown).

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\frontend\src\InteractiveSearch\`

## Files

| File | Purpose |
|------|---------|
| `InteractiveSearch.tsx` | Main modal with sortable/filterable release list |
| `InteractiveSearchRow.tsx` | One release row (title, indexer, size, peers, quality, language, custom format score, rejections) |
| `InteractiveSearchFilterModal.tsx` | Filter popup |
| `InteractiveSearchPayload.ts` | Discriminated union for what to search. **Phase 7 Plan 07-05** extended with `ChapterSearchPayload` (`{ chapterId }`) + `MangaSearchPayload` (`{ mangaId }`) variants per RESEARCH Lock #14. |
| `InteractiveSearchType.ts` | enum: `'episode' \| 'season' \| 'chapter' \| 'manga'` (Phase 7 Plan 07-05 added the `chapter` + `manga` literals). |
| `Peers.tsx` | Seeders/peers display |
| `ReleaseSceneIndicator.tsx` | Scene release indicator |
| `releaseOptionsStore.ts` | Zustand: search options |
| `useReleases.ts` | API hook (`useReleases({ seriesId, seasonNumber?, episodeId? })`). **Phase 7 Plan 07-05** added `getReleasePath()` discriminator: chapter / manga payloads route to `/api/v5/manga/release` (Phase 6 endpoint), TV variants stay on `/release`. |

## Subdirectory: OverrideMatch/

| File | Purpose |
|------|---------|
| `OverrideMatchModal.tsx` / `OverrideMatchModalContent.tsx` | "I want to override the auto-match" — let user manually choose series/episode mapping for a release |
| `OverrideMatchData.tsx` | Display form |
| `DownloadClient/SelectDownloadClientModal.tsx` / `SelectDownloadClientModalContent.tsx` / `SelectDownloadClientRow.tsx` | Pick which download client |

## Flow

```
User opens series/episode detail → clicks "Interactive Search" toolbar button
        ↓
Modal opens with InteractiveSearch component
        ↓
useReleases() → POST /api/v5/release { seriesId, episodeId? }
        ↓ Backend runs the same indexer search the auto pipeline uses,
        ↓ but returns ALL results (with rejections) — does not auto-grab.
        ↓
List<ReleaseResource> with: title, indexer, age, size, seeders, peers,
quality, languages, customFormats, customFormatScore, mappedSeriesId,
mappedEpisodeIds, rejections, releaseGroup, sceneSource, sceneMapping
        ↓
User reviews, sorts (default: by quality + custom format), filters out rejected
        ↓
User clicks "Grab" on a row → POST /api/v5/release { guid, indexerId }
        ↓
Backend submits to download client just like auto pipeline
```

## Override Match

If the parser mismatched the release (e.g., picked the wrong series), the user can use **Override Match** to manually point the release at the correct series/episode + force-grab.

## Manga Adaptation Notes

Phase 7 Plan 07-05 extends this directory in place rather than parallel-forking
(D-01 — InteractiveSearch is shared+extended infra, not a media-type-shaped page
dir). Volumes are NOT adapted (PROJECT.md Volumes/Seasons Out-of-Scope).

Adapted in Plan 07-05:
- `InteractiveSearchPayload.ts` — added `ChapterSearchPayload` (`{ chapterId }`)
  + `MangaSearchPayload` (`{ mangaId }`) variants. TV `EpisodeSearchPayload` /
  `SeasonSearchPayload` preserved verbatim.
- `InteractiveSearchType.ts` — added `'chapter'` + `'manga'` literals.
- `useReleases.ts` — added `getReleasePath(payload)` discriminator routing
  chapter/manga payloads to `/api/v5/manga/release` (Phase 6 endpoint).
- `<InteractiveSearch type="chapter" searchPayload={{ chapterId }} />` is the
  caller pattern from `Manga/Details/MangaDetails.tsx` Search tab.

Still pending Phase 8 cleanup:
- Result row column extension (Page Count, Scanlation Group, etc.) — deferred.
- Override Match `chapter` mapping — deferred.

## Cross-References

- [../../CLAUDE.md](../../CLAUDE.md) — Frontend overview
- [../../../src/NzbDrone.Core/IndexerSearch/](../../../src/NzbDrone.Core/IndexerSearch/) — Backend search criteria
- [../../../src/NzbDrone.Core/DecisionEngine/CLAUDE.md](../../../src/NzbDrone.Core/DecisionEngine/CLAUDE.md) — Backend that emits rejection reasons
- [../../../src/Sonarr.Api.V5/Release/](../../../src/Sonarr.Api.V5/Release/) — REST endpoints
- [../InteractiveImport/CLAUDE.md](../InteractiveImport/CLAUDE.md) — Sibling: import existing files manually
