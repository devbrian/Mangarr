# ImportLists

## Purpose

**Import lists** automatically add new manga to the library by ingesting from external lists (MangaDex follows, AniList lists, MyAnimeList lists, custom JSON feeds, etc.). Uses the **ThingiProvider** plugin pattern — providers are auto-discovered via reflection scan of `NzbDrone.Core` types implementing `IMangaImportList`.

Phase 26 (Plan 26-04) ships the **substrate backend only** per D-08 — contract, abstract base classes, the 3-repo persistence layer, the sync orchestrator, and the `MangaDeletedEvent` event-driven exclusion handler. **Phase 26 ships ZERO production providers** — the Settings → ImportLists Add picker is empty in production until Phase 27 plugs in the first MangaDex / AniList / MyAnimeList plugin additively against this seam.

**Heritage:** Sonarr-fork shape preserved verbatim (RESTORE + AUTHOR pattern per v1.1 SUMMARY #2). Reference slice at `.planning/reference/sonarr-vertical-slices/import-lists/` retains the upstream Sonarr files for line-by-line port traceability.

**Absolute Path:** `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\ImportLists\`

## Key Files

| File | Purpose |
|------|---------|
| `IMangaImportList.cs` | Provider contract — `IProvider` peer for manga import-list providers; `ImportListType ListType`, `TimeSpan MinRefreshInterval`, `ImportListFetchResult Fetch()` |
| `ImportListBase.cs` | Abstract base — `ImportListBase<TSettings>` carries DI ctor, `DefaultDefinitions`, `CleanupListItems` dedup (manga-ID triplet), `Test()` shell |
| `HttpImportListBase.cs` | HTTP-driven abstract base — `FetchItems` exception chain (WebException / TooManyRequestsException / HttpException / CloudFlareCaptchaException / RequestLimitReachedException) + paging + `TestConnection` |
| `IImportListSettings.cs` | Settings interface — `IProviderConfig` + `BaseUrl` shape that every provider's `TSettings : IImportListSettings, new()` honors |
| `ImportListSettingsBase.cs` | Abstract `ImportListSettingsBase<TSettings>` — Equ memberwise equality helper for settings POCOs |
| `ImportListDefinition.cs` | Persisted config row — `EnableAutomaticAdd` / `ShouldMonitor` (the single canonical 7-value `MangaMonitor` since #357; the Sonarr-inherited `MonitorTypes` + `NewItemMonitorTypes` enums are deleted) / `TranslationProfileId` / `CustomFormatProfileId` / `RootFolderPath` / `Tags`. `MonitorNewItems` is retained as an INTERNAL field only (#356 — never surfaced on the V5 resource or read by the sync service; it persists the `NotNullable` DB column's default since BasicRepository builds INSERTs from POCO properties). New-chapter monitoring is derived from `ShouldMonitor` via `MangaMonitorExtensions.DeriveMonitorNewItems`. |
| `ImportListStatus.cs` | Per-provider escalation/backoff state + `LastInfoSync` / `HasRemovedItemSinceLastClean` |
| `ImportListItemInfo.cs` | Fetched-item POCO (manga-ID triplet shape: `MangaDexId` string / `MalId` int? / `AniListId` int?) |
| `ImportListType.cs` | Enum: `{ Program, Other, Advanced }` — Plex/Trakt/Simkl values dropped per Pitfall 6 |
| `ImportListFactory.cs` | D-13 repo #1 consumer — `ProviderFactory<IMangaImportList, ImportListDefinition>`; `AutomaticAddEnabled(bool filterBlocked = true)` |
| `ImportListRepository.cs` | D-13 repo #1 — `ProviderRepository<ImportListDefinition>` with `UpdateSettings(model)` + `FindByName(name)` |
| `ImportListStatusRepository.cs` | D-13 repo #2 — `ProviderStatusRepository<ImportListStatus>` |
| `ImportListStatusService.cs` | Escalation/backoff + `GetListStatus` / `UpdateListSyncStatus` / `MarkListsAsCleaned` |
| `ImportListSyncCommand.cs` | Command — `int? DefinitionId` + `SendUpdatesToClient = true` + `UpdateScheduledTask` gated on null DefinitionId |
| `ImportListSyncService.cs` | `IExecute<ImportListSyncCommand>` orchestrator — per-item exclusion + already-in-library filter + bulk `IAddMangaService.AddManga(List<Manga>, true)` |
| `ImportListUpdatedHandler.cs` | `IHandle<ProviderUpdatedEvent<IMangaImportList>>` — queues a single-list `ImportListSyncCommand` on definition edit |
| `FetchAndParseImportListService.cs` | Parallel per-provider fetch with `MinRefreshInterval` gate + cross-list dedup (manga-ID triplet) |
| `ImportListPageableRequest.cs` / `…Chain.cs` / `ImportListRequest.cs` / `ImportListResponse.cs` | Paging primitives (verbatim ports) |
| `IImportListRequestGenerator.cs` / `IProcessImportListResponse.cs` | Provider extension points (request gen + response parse) |
| `TolerantEnumConverter.cs` | JSON enum parser — verbatim port; gracefully handles unknown enum values |
| `ListSyncLevelType.cs` | Library-cleanup level enum — `{ Disabled, LogOnly, KeepAndUnmonitor, KeepAndTag }` |
| `Exceptions/ImportListException.cs` | Provider error wrapper carrying the `ImportListResponse` |
| `Exclusions/ImportListExclusion.cs` | D-13 repo #3 entity — manga-ID triplet exclusion row (`MangaDexId` string / `MalId` int? / `AniListId` int? / `Title`) |
| `Exclusions/ImportListExclusionRepository.cs` | D-13 repo #3 — `BasicRepository<ImportListExclusion>` + `FindByMangaDexId(string)` |
| `Exclusions/ImportListExclusionService.cs` | D-12 event-driven auto-add — `IHandle<MangaDeletedEvent>` |
| `ImportListItems/ImportListItemRepository.cs` | Per-list-cache repo — `GetAllForLists(List<int>)` |
| `ImportListItems/ImportListItemService.cs` | Per-list-cache service — `SyncMangaForList`, `IHandleAsync<ProviderDeletedEvent<IMangaImportList>>` cascade cleanup |

## Patterns / Conventions

### ThingiProvider auto-discovery (no manual DI)

Production reflection scans `NzbDrone.Core` for types implementing `IMangaImportList` and binds them into the `IEnumerable<IMangaImportList>` ctor parameter of `ImportListFactory`. Phase 26 ships **zero** concrete providers — the scan returns 0 implementations. Phase 27 lands the first plugin against this seam.

The test-only `TestImportList` fake lives in `NzbDrone.Core.Test/ImportListTests/Fakes/` so production reflection-scan does NOT see it (D-09 / Pitfall 2). The bucket A SC#6 anti-prod-leak gate (`ImportListFactoryFixture.factory_returns_zero_providers_on_empty_di_bag`) is the static enforcement.

### D-13 — 3-separate-Dapper-repo split

Sonarr-canonical per the 2026-05-19 sonarr-consistency-audit:
- `ImportListRepository : ProviderRepository<ImportListDefinition>` — Definition CRUD + JSON Settings hydration (inherits CR-02 SQLITE_BUSY retry from `ProviderRepository<T>.Query`).
- `ImportListStatusRepository : ProviderStatusRepository<ImportListStatus>` — per-provider escalation/backoff.
- `ImportListExclusionRepository : BasicRepository<ImportListExclusion>` — exclusion CRUD + `FindByMangaDexId(string)` finder.

Do NOT collapse to a single repo — the three base-class inheritance chains drive disjoint contracts (Definition has Settings hydration; Status has FindByProviderId / DeleteByProviderId; Exclusion has the BasicRepository pattern).

### D-12 — event-driven `IHandle<MangaDeletedEvent>` (NOT direct call)

`ImportListExclusionService.Handle(MangaDeletedEvent)` is the ONLY auto-add entry point. `MangaController.Delete` MUST publish `MangaDeletedEvent` and let the handler do its work — it MUST NOT call `_importListExclusionService.Add(...)` synchronously. The `MangaDeletedEvent.AddImportListExclusion` flag (default `true`) lets bulk-delete callers opt out (admin tooling, programmatic resyncs).

### D-15 — `TaskManager.defaultTasks` 24h cadence row

`ImportListSyncCommand` is registered at the Sonarr-canonical 24h cadence (`Interval = 24 * 60` minutes). Restored by Plan 26-04; the Phase 15 D-26 strip-comment was physically deleted in the same edit. The `TaskManager.cs:17` `using NzbDrone.Core.ImportLists;` import was un-commented in the same atomic commit.

## Manga Adaptation Notes

### TVDB / IMDB / TMDB → MangaDexId / MalId / AniListId

The Sonarr reference uses a `TvdbId` (with optional `ImdbId` / `TmdbId`) as the canonical cross-source ID. Mangarr replaces this with the manga-ID triplet:

| Sonarr field | Mangarr peer | Type | Notes |
|--------------|--------------|------|-------|
| `TvdbId` (int) | `MangaDexId` (string) | required-ish | Persisted as the canonical Guid string serialization to match Migration 003's `.AsString().Nullable()` column. `Manga.MangaDexId` is `Guid?` on the aggregate POCO; exclusion rows store `manga.MangaDexId?.ToString()`. |
| `ImdbId` (string) | (dropped) | — | No manga peer; SkyHook deleted in Phase 15. |
| `TmdbId` (int) | (dropped) | — | No manga peer. |
| n/a | `MalId` (int?) | nullable | MyAnimeList ID; AniList-only lists may carry this. |
| n/a | `AniListId` (int?) | nullable | AniList GraphQL ID. |

`CleanupListItems` dedup key swapped from `(Title, TvdbId, ImdbId)` to `(Title, MangaDexId, MalId, AniListId)`.

### ImportListType enum trim (Pitfall 6)

The reference enum carries `{ Program, Plex, Trakt, Simkl, Other, Advanced }`. Mangarr trims to `{ Program, Other, Advanced }` — Plex/Trakt/Simkl have no manga peers. Phase 27 adds `MangaDex`, `AniList`, `MyAnimeList` values as each concrete provider lands. Quick task 260608-vf9 appends `MyAnimeListStack` (public Interest-Stack scrape provider).

### Concrete providers

| Provider dir | Type | Auth | Notes |
|--------------|------|------|-------|
| `MangaDex/` | `MangaDexImportList` (OAuth) | password grant | Phase 27 Plan 27-02 — `/user/follows/manga`. See [MangaDex/CLAUDE.md](./MangaDex/CLAUDE.md). |
| `MyAnimeListStack/` | `MyAnimeListStackImportList` (HTTP scrape) | none (public) | Quick task 260608-vf9 — scrapes a public MyAnimeList Interest-Stack page (`/stacks/{id}`) → `ImportListItemInfo` with `MalId`. `Test()` rejects an Anime stack. Adds `ImportListType.MyAnimeListStack`. See [MyAnimeListStack/CLAUDE.md](./MyAnimeListStack/CLAUDE.md). |

### Sonarr OAuth Settings POCO pattern (Phase 27 territory)

Trakt's Sonarr Settings POCO uses `[FieldDefinition(Hidden = HiddenType.Hidden, Privacy = PrivacyLevel.Password)]` for the OAuth tokens — Phase 27 providers will mirror this verbatim. The reference slice's Trakt files at `.planning/reference/sonarr-vertical-slices/import-lists/` (parent of this Mangarr substrate) are the authoritative port source.

### Cross-source ID resolution (primary-aware since quick-260608-vf9)

`ImportListSyncService.ProcessListItems` resolves an item to the **active primary metadata source's own id** before staging it for add — because `AddMangaService.PrepareForAdd → ResolveSourceIdForPrimary` REQUIRES that id (e.g. `MangaBakaId` when MangaBaka is primary) or it throws *"no source ID for active primary"*.

- **Primary = MangaDex (or unconfigured → legacy default):** the original MangaDexId-centric path runs. An item already carrying a `MangaDexId` adds directly; a MAL/AniList-only item is resolved to a `MangaDexId` via `MangaDex.SearchForNewManga(title)` (strict-AND id agreement) or skipped.
- **Primary = MangaBaka / AniList / MyAnimeList (non-MangaDex):** `StageViaPrimaryResolution` resolves the item via `primary.SearchForNewManga(title)`, matches on the alt id(s) the item carries (`MalId`/`AniListId`), and stages the matched candidate carrying the primary's id. A MangaDexId-only item cannot resolve here (MangaBaka results don't expose a MangaDexId per MetadataSource D-03a) and is skipped — MangaDex import lists belong under a MangaDex primary.

The blocker fixed in quick-260608-vf9: pre-fix the method hardcoded `if (match?.MangaDexId != null)`, so under the v1.3 default MangaBaka primary EVERY MAL/AniList-only item (incl. the MyAnimeList Stack list) was silently rejected because MangaBaka search results carry `MangaBakaId` (not `MangaDexId`). The 429 throttle latch + already-in-library short-circuit are preserved in both paths.

## Cross-References

- `.planning/phases/26-importlist-substrate-migration-003-anilist-transport-refacto/26-CONTEXT.md` — load-bearing decisions D-01..D-15
- `.planning/phases/26-importlist-substrate-migration-003-anilist-transport-refacto/26-RESEARCH.md` — 10-question deep dive
- `.planning/phases/26-importlist-substrate-migration-003-anilist-transport-refacto/26-PATTERNS.md` — file-by-file translation guide
- `.planning/reference/sonarr-vertical-slices/import-lists/` — Sonarr upstream reference (RESTORE + AUTHOR source)
- [../Indexers/CLAUDE.md](../Indexers/CLAUDE.md) — analog vertical (ThingiProvider + ProviderFactory + ProviderRepository + ProviderStatusRepository — same shape pattern)
- [../Manga/Events/MangaDeletedEvent.cs](../Manga/Events/MangaDeletedEvent.cs) — D-12 hook (the `AddImportListExclusion` bool drives the event-driven auto-add)
- [../Jobs/TaskManager.cs](../Jobs/TaskManager.cs) — D-15 row at 24h cadence
- [../Datastore/Migration/003_v1_1_importlist_substrate_delayprofile_trim.cs](../Datastore/Migration/003_v1_1_importlist_substrate_delayprofile_trim.cs) — schema reshape (TVDB/IMDB → manga-ID triplet, QualityProfileId → TranslationProfileId + CustomFormatProfileId, SearchForMissingEpisodes → SearchForMissingChapters)
