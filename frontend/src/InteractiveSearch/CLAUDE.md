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
| `InteractiveSearchPayload.ts` | Type for what to search (series, season, episode, etc.) |
| `InteractiveSearchType.ts` | enum: `series` / `season` / `episode` |
| `Peers.tsx` | Seeders/peers display |
| `ReleaseSceneIndicator.tsx` | Scene release indicator |
| `releaseOptionsStore.ts` | Zustand: search options |
| `useReleases.ts` | API hook (`useReleases({ seriesId, seasonNumber?, episodeId? })`) |

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

The architecture transfers cleanly. Updates needed:
- `InteractiveSearchType` enum: add `chapter` and `volume` (replace `episode` and `season`)
- `useReleases` payload changes: `chapterId`, `volumeNumber` instead of `episodeId`/`seasonNumber`
- Result row columns: add Page Count, Scanlation Group, Color, etc.; drop Resolution
- Override match needs `chapter` mapping instead of `episode`

## Cross-References

- [../../CLAUDE.md](../../CLAUDE.md) — Frontend overview
- [../../../src/NzbDrone.Core/IndexerSearch/](../../../src/NzbDrone.Core/IndexerSearch/) — Backend search criteria
- [../../../src/NzbDrone.Core/DecisionEngine/CLAUDE.md](../../../src/NzbDrone.Core/DecisionEngine/CLAUDE.md) — Backend that emits rejection reasons
- [../../../src/Sonarr.Api.V5/Release/](../../../src/Sonarr.Api.V5/Release/) — REST endpoints
- [../InteractiveImport/CLAUDE.md](../InteractiveImport/CLAUDE.md) — Sibling: import existing files manually
