# NzbDrone.Core/Blocklisting

## Purpose

Release-level blocklist — when a download fails terminally (or the user manually clicks "Block & Search"), the release identity is recorded so future search results matching the same identity are auto-skipped by the Decision Engine's `BlocklistSpecification`.

Like history, blocklisting is an **event-driven write surface** (RESEARCH §Anti-Pattern: NEVER write blocklist rows from inside the Repository). The service subscribes to download-pipeline failure events and writes one row per terminal failure.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Blocklisting\`

## Top-Level Files

| File | Purpose |
|------|---------|
| `Blocklist.cs` | TV blocklist row entity. Carries `SeriesId / EpisodeIds / SourceTitle / Quality / Languages / Date / PublishedDate / Size / Protocol / Indexer / Message / TorrentInfoHash`. **Release identity** = `(Title, Indexer, Protocol, TorrentInfoHash)` quintuple. |
| `BlocklistRepository.cs` | Dapper `BasicRepository<Blocklist>`. Custom queries: `BlocklistedByTitle`, `BlocklistedByTorrentInfoHash`, `BlocklistedBySeries`, `DeleteForSeries`. |
| `BlocklistService.cs` | Event-driven service. Subscribes to: `DownloadFailedEvent` (auto-blocklist), `SeriesDeletedEvent` (cascade). Implements `IExecute<ClearBlocklistCommand>` for the "Clear blocklist" UI button. |
| `ClearBlocklistCommand.cs` | UI button command. |

## Phase 6 Manga Sibling

Phase 6 Plan 06-04 ships `Blocklisting/Manga/` as a parallel sibling. **The separate sibling table is the BL-01-style mechanical guarantee** — `MangaBlocklist` is registered as a separate `Mapper.Entity<MangaBlocklist>` in `TableMapping.cs` (line 123 in DIVERGENCE.md) so Dapper queries cannot cross-contaminate even when manga and series int IDs collide.

| Sibling | Phase 6 Plan | Notes |
|---------|--------------|-------|
| [`Blocklisting/Manga/`](./Manga/CLAUDE.md) | Plan 06-04 | Manga-side blocklist. **`MangaBlocklist` + `MangaBlocklistRepository`**: parallel sibling to `Blocklist` + `BlocklistRepository`. **D-11 release-identity triple**: `(SourceKey, ReleaseGuid, SourceTitle)` — distinct from TV's `(Title, Indexer, Protocol, TorrentInfoHash)` quintuple. **Drops** `Quality` (no quality model per Phase 5 D-04), `Protocol` (manga is `DownloadProtocol.Http` only in v1), `TorrentInfoHash` (no torrent path). **Adds** `ReleaseInfoJson` for full release-info round-trip. **`IMangaBlocklistService` + `MangaBlocklistService`**: event-driven service with `IHandle<ChapterDownloadFailedEvent>` (auto-blocklist using D-11 triple from Plan 06-01's extended event shape) + `IHandleAsync<MangaDeletedEvent>` (cascade) + `IExecute<ClearMangaBlocklistCommand>` for the "Clear blocklist" UI button. **PITFALL 5 mitigation in `Blocklisted(int, ReleaseInfo)`**: trim + lowercase Title both sides + OrdinalIgnoreCase string compares + null-tolerant SourceKey fallback (when either side null/empty, match-on-Title-Guid only). Tolerates null `Release` on legacy 4-arg `ChapterDownloadFailedEvent` emit sites — falls back to `event.Message` as SourceTitle. **`MangaBlocklistAddedEvent`**: NO TV analog — created specifically as the post-Insert ordering hook for Plan 06-08 `AutoRetryOrchestrator` (subscribing to `ChapterDownloadFailedEvent` directly would race against `_repository.Insert(...)`). **ORDERING INVARIANT (D-19)**: `_repository.Insert(...)` runs FIRST, then `_eventAggregator.PublishEvent(new MangaBlocklistAddedEvent(...))` — synchronous fan-out guarantees Plan 06-08 sees the committed row. Mirrored on the manual `Block(...)` path so the contract is uniform. **`ClearMangaBlocklistCommand`**: manga sibling of `ClearBlocklistCommand`. Phase 8 cleanup: collapse with `Blocklist` + `BlocklistRepository` + `BlocklistService` + `ClearBlocklistCommand`. |
| `DecisionEngine/Manga/Specifications/BlocklistSpecification.cs` STUB body replacement | Plan 06-04 | Replaces the Phase 5 Accept-always STUB body with `_mangaBlocklistService.Blocklisted(subject.Manga.Id, subject.Release)` + `Decision.Reject(Blocklisted, "Release is blocklisted")` when matched. Pitfall 5 matching logic lives in the service so the spec stays a thin gate. |

## D-11 Release-Identity Triple

The TV blocklist matches on `(Title, Indexer, Protocol, TorrentInfoHash)`. Manga has no protocol diversity in v1 (only `DownloadProtocol.Http`) and no torrent info hash; the triple is `(SourceKey, ReleaseGuid, SourceTitle)`:

- **`SourceKey`**: from `ReleaseInfo.Indexer` per Phase 3 D-17 + Plan 06-03 ChapterHistoryService precedent (line 141). The wire-layer source is `release.Indexer`; the column is named `SourceKey` for canonical clarity.
- **`ReleaseGuid`**: stable per-release identity (e.g., MangaDex chapter UUID). Survives URL/title rewrites.
- **`SourceTitle`**: human-readable release title from the release wire shape. Used as fallback when either of the above is null.

**Group-level blocking** is expressible via the existing `-10000 ScanlationGroup` Custom Format (Phase 5 D-09) — no separate "block this group" UI in v1.

## Pitfall 5: Title Normalization

Real-world manga release titles are notoriously inconsistent in casing, whitespace, and Unicode form. `Blocklisted(...)` normalizes both sides:

```csharp
var aTitle = (a ?? string.Empty).Trim().ToLowerInvariant();
var bTitle = (b ?? string.Empty).Trim().ToLowerInvariant();
if (string.Equals(aTitle, bTitle, StringComparison.OrdinalIgnoreCase)) return true;
```

If either side's `SourceKey` is null or empty, the comparison falls back to title + GUID only.

## Manga Adaptation Notes

The blocklist pattern transfers verbatim (event-driven service writes; spec reads). The two divergences are:
1. **D-11 release-identity triple** vs TV's quintuple — manga has no protocol/hash diversity.
2. **`MangaBlocklistAddedEvent` ordering hook** has no TV peer — TV's `BlocklistService` inserts directly because its sole consumer (`BlocklistSpecification`) only reads at decision time. Mangarr's auto-retry orchestrator (Plan 06-08) needs the explicit ordering hook to subscribe AFTER the row is committed.

Phase 8 collapses the sibling tables into a unified `Blocklisting` namespace when domain rename runs.

## Cross-References

- [../CLAUDE.md](../CLAUDE.md) — NzbDrone.Core overview
- [../Messaging/CLAUDE.md](../Messaging/CLAUDE.md) — Events that blocklisting consumes
- [../DecisionEngine/CLAUDE.md](../DecisionEngine/CLAUDE.md) — `BlocklistSpecification` (TV) + manga `BlocklistSpecification` (Plan 06-04 STUB body replacement) consume the blocklist
- [../Download/CLAUDE.md](../Download/CLAUDE.md) — `Download/Manga/AutoRetryOrchestrator.cs` (Plan 06-08) subscribes to `MangaBlocklistAddedEvent` (NOT `ChapterDownloadFailedEvent` — anti-race contract)
- [../../Mangarr.Api.V5/CLAUDE.md](../../Mangarr.Api.V5/CLAUDE.md) — `Manga/Blocklist/MangaBlocklistController.cs` exposes the manga blocklist over REST (BLOCK-01..02)
