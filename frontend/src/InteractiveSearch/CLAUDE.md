# InteractiveSearch/

## Purpose

**Manual release search** — user clicks "Search" on a manga or chapter, sees all releases the gateway returns, and manually picks one to download. Bypasses the automated DecisionEngine ranking (though rejection reasons are still shown).


## Files

| File | Purpose |
|------|---------|
| `InteractiveSearch.tsx` | Main modal with sortable/filterable release list |
| `InteractiveSearchRow.tsx` | One release row (title, indexer, source, translated language, scanlation group, votes, custom format score, rejections). The **Votes** column (between scanlation group and custom format score) shows the gateway-supplied per-release vote count, plumbed via `ReleaseInfo.Votes` for a future highest-votes Custom Format; renders `votes ?? 0`. Column order in `releaseOptionsStore.ts` MUST stay aligned with the cell order here. |
| `InteractiveSearchFilterModal.tsx` | Filter popup |
| `InteractiveSearchPayload.ts` | Manga-only discriminated union: `ChapterSearchPayload` (`{ kind: 'chapter', chapterId }`) + `MangaSearchPayload` (`{ kind: 'manga', mangaId }`). The TV-shape `EpisodeSearchPayload` / `SeasonSearchPayload` variants were retired in issue #263. `searchPayload.kind` is the single routing discriminator (the redundant `InteractiveSearchType` prop + file were deleted in issue #263). |
| `Peers.tsx` | Seeders/peers display |
| `releaseOptionsStore.ts` | Zustand: search options |
| `useReleases.ts` | Search hook (`useReleases(payload: InteractiveSearchPayload)` → GET `/api/v5/manga/release`, Phase 6 `MangaReleaseController`; the manga-only union means both chapter and manga kinds query the same endpoint; the TV `/release` path was retired in issue #263). Also exports `useGrabMangaRelease()`, which POSTs the manga-shape grab body to the same endpoint. |

## Subdirectory: OverrideMatch/

| File | Purpose |
|------|---------|
| `Chapter/ChapterOverrideMatchModal.tsx` / `ChapterOverrideMatchModalContent.tsx` | Chapter-flavor override-match (chapterIds + mangaId=0 wire sentinel) |
| `Manga/MangaOverrideMatchModal.tsx` / `MangaOverrideMatchModalContent.tsx` | Manga-flavor override-match (mangaId) |
| `OverrideMatchData.tsx` | Shared per-row "selected value + change-button" presenter (used by both Chapter + Manga content bodies) |
| `OverrideMatchModalContent.css` | Shared CSS module for the override-match content bodies (the TV-shape `OverrideMatchModalContent.tsx` it was named for was retired in issue #263; the stylesheet survives as shared styling) |
| `DownloadClient/SelectDownloadClientModal.tsx` / `SelectDownloadClientModalContent.tsx` / `SelectDownloadClientRow.tsx` | Pick which download client |

The TV-shape `OverrideMatchModal.tsx` + `OverrideMatchModalContent.tsx` fallback was deleted in issue #263 (the `InteractiveSearch` union became manga-only, so the 3-way row discriminator in `InteractiveSearchRow.tsx` collapsed to 2-way).

## Flow

```
User opens manga or chapter detail → clicks "Interactive Search" toolbar button
        ↓
Modal opens with InteractiveSearch component
        ↓
useReleases(payload: InteractiveSearchPayload) → GET /api/v5/manga/release { kind, chapterId | mangaId }
        ↓ Backend runs the same gateway search the auto pipeline uses,
        ↓ but returns ALL results (with rejections) — does not auto-grab.
        ↓
List<ReleaseResource> with: title, indexer, source, translatedLanguage,
scanlationGroup, votes, customFormats, customFormatScore, mappedMangaId,
mappedChapterIds, rejections
        ↓
User reviews, sorts (default: by translated language + custom format), filters out rejected
        ↓
User clicks "Grab" on a row → POST /api/v5/manga/release { guid, indexerId } (useGrabMangaRelease)
        ↓
Backend submits to download client just like auto pipeline
```

## Override Match

If the parser mismatched the release (e.g., picked the wrong manga), the user can use **Override Match** to manually point the release at the correct manga/chapter + force-grab.

## Manga Adaptation Notes

Phase 7 Plan 07-05 extends this directory in place rather than parallel-forking
(D-01 — InteractiveSearch is shared+extended infra, not a media-type-shaped page
dir). Volumes are NOT adapted (PROJECT.md Volumes/Seasons Out-of-Scope).

Current shape (Phase 7 Plan 07-05, then narrowed to manga-only in issue #263):
- `InteractiveSearchPayload.ts` — `ChapterSearchPayload` (`{ kind: 'chapter', chapterId }`)
  + `MangaSearchPayload` (`{ kind: 'manga', mangaId }}`); `payload.kind` is the sole
  discriminator. The TV `EpisodeSearchPayload` / `SeasonSearchPayload` variants and the
  separate `InteractiveSearchType.ts` file were both deleted in issue #263.
- `useReleases.ts` — routes every payload to `/api/v5/manga/release` (Phase 6 endpoint).
- `<InteractiveSearch searchPayload={{ kind: 'chapter', chapterId }} />` is the
  caller pattern from `Chapter/ChapterDetailsModal.tsx` / `Manga/Details/MangaDetails.tsx`
  Search tab (the `type` prop was retired in issue #263).

Result row columns shipped: Source, Translated Language, Scanlation Group, and Votes
(`InteractiveSearchRow.tsx` renders `ReleaseInfo.Votes` as `votes ?? 0`; the
`releaseOptionsStore.ts` column order must stay aligned with the row cell order).

Still pending Phase 8 cleanup:
- Remaining result-row column extensions beyond the shipped set (e.g. Page Count) — deferred.
- Override Match `chapter` mapping — deferred.

## Stable test attributes

`InteractiveSearchRow.tsx` emits two stable HTML attributes on every release
row's `<TableRow>` element. Both are render-only metadata (no new state,
no new fetch, no new prop) and are the durable selectors Playwright fixtures
target instead of brittle CSS-class / text matches.

| Attribute | Value | Contract |
|-----------|-------|----------|
| `data-testid` | `interactive-search-row-{guid}` | Per-row testid keyed by release `guid` (Phase 18 Plan-08 D-18). PageObjects target individual rows via `Locator("[data-testid='interactive-search-row-{guid}']")`. |
| `data-source` | `{release.indexer}` verbatim (e.g. `MangaDex`, `Comix`) | Originating `IndexerDefinition.Name` (Phase 33 D-11). Reflects the live `release.indexer` API field — state-not-rendering compliant per `scripts/audit-test-assertions.sh`. Empty/missing `indexer` renders as `data-source=""` (absence is itself a signal — no conditional branch). |

**Consumers (Plan 33-04):**
- `src/NzbDrone.Automation.Test/Tests/InteractiveSearch/InteractiveSearchModalFixture.cs`
- `src/NzbDrone.Automation.Test/Tests/InteractiveSearch/InteractiveSearchGrabFixture.cs`
- `src/NzbDrone.Automation.Test/Tests/InteractiveSearch/InteractiveSearchOpenFixture.cs`

Plan 33-04 asserts at least one `data-source='Comix'` row alongside the
existing MangaDex assertions — proves the cassetting signer's payload flows
end-to-end (signer → indexer → DecisionEngine → API → React modal) per
`feedback_verify_ui_state_not_just_rendering` memory.

**Do not strip either attribute.** The 5 additional `data-testid` emissions
on inner cells (`{rowTestId}-title`, `{rowTestId}-decision`,
`{rowTestId}-rejected-icon`, `{rowTestId}-grab-button`,
`interactive-search-row-override-trigger`) are also stable test contracts
documented inline in `InteractiveSearchRow.tsx` at the `rowTestId` comment
block (lines ~201-212).

## Cross-References

- [../../CLAUDE.md](../../CLAUDE.md) — Frontend overview
- [../../../src/NzbDrone.Core/IndexerSearch/](../../../src/NzbDrone.Core/IndexerSearch/) — Backend search criteria
- [../../../src/NzbDrone.Core/DecisionEngine/CLAUDE.md](../../../src/NzbDrone.Core/DecisionEngine/CLAUDE.md) — Backend that emits rejection reasons
- [../../../src/Mangarr.Api.V5/Manga/Release/](../../../src/Mangarr.Api.V5/Manga/Release/) — REST endpoints
- [../InteractiveImport/CLAUDE.md](../InteractiveImport/CLAUDE.md) — Sibling: import existing files manually
