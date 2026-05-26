# Indexers/MangaDex (Phase 3)

## Purpose

v1 BEDROCK manga indexer. Direct port from MangaDex's published API documentation at
<https://api.mangadex.org/docs> (NOT keiyoushi/extensions-source — `THIRD-PARTY-NOTICES.md`
documents the distinction). Shares `SourceKey="mangadex"` rate budget with the Phase 2
`MangaDexMetadataSource` instance — Phase 1 D-11/D-12 single-budget-per-SourceKey contract.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Indexers\MangaDex`

## Key Files

| File | Purpose |
|------|---------|
| `MangaDexIndexer.cs` | Concrete `HttpAggregatorBase<MangaDexIndexerSettings>` plugin. Implements `Fetch(MangaSearchCriteria)` + `Fetch(ChapterSearchCriteria)`; the 7 inherited TV overloads throw `NotSupportedException` (D-03); `GetDownloadHeaders` returns empty (D-14 — no Referer needed for `/at-home/server`). Constructor injects `IIndexerSourceStatusService` (Plan 03-03 D-17 sibling) so a future failure-pressure escalation pathway can record per-`SourceKey` state. |
| `MangaDexIndexerSettings.cs` | `IHttpAggregatorSettings` impl. **`UserAgentOverride` has NO `[FieldDefinition]`** — T-CONFIG-DRIFT-01 mitigation by absence; Pitfall 4. Reflection fixture `MangaDexIndexerSettingsHonestUaFixture` enforces. Default `RateSeconds=1.5` (40 req/min — matches `/at-home/server` strictest limit). |
| `MangaDexRequestGenerator.cs` | URL composition: `/chapter` (FetchRecent) + `/manga/{MangaDexId}/feed` (search, paginated `limit=500`). 7 TV overloads return empty `IndexerPageableRequestChain` (D-03 fan-out). When `MangaDexId` is null (cross-resolve gap from Phase 2 D-19..D-22 fallback), the chain is empty and `FetchReleases` short-circuits. |
| `MangaDexParser.cs` | JSON parser (Newtonsoft via `JsonConvert.DeserializeObject<ChapterFeedResource>`). Populates `ReleaseInfo.ScanlationGroup` (from `relationships[scanlation_group].attributes.name`) and `ReleaseInfo.TranslatedLanguage` (BCP-47 from `attributes.translatedLanguage`) per Plan 03-02 Q-4 / SOURCE-04. `decimal.TryParse(InvariantCulture)` for chapter number per Phase 2 D-12 + Pitfall 7. Sets `DownloadUrl=https://api.mangadex.org/at-home/server/{chapterId}` — the chapter MANIFEST URL (NOT a single image; Phase 4 dereferences). |
| `MangaDexIndexerApi.cs` | URL-building helpers; sibling to Phase 2 `MangaDexApi.cs` (Phase 8 cleanup symmetry). `ApplyHeaders` sets `RateLimitKey="mangadex"` (Phase 1 D-11) + honest UA + JSON Accept for ad-hoc callers. |

## Patterns / Conventions

- Reuses Phase 2's `ChapterFeedResource` DTO (`MetadataSource/MangaDex/Resource/`) — Phase 8 cleanup will collapse the DTO into a single `Indexers/MangaDex/Resource/` directory.
- ThingiProvider auto-discovery: registers automatically by extending `HttpAggregatorBase<TSettings>` — no manual DI wiring.
- Rate budget: `RateSeconds=1.5` (40 req/min) matches MangaDex `/at-home/server` strictest limit; honoring this prevents Phase 4 image-fetch sharing the SourceKey bucket from bursting.
- Honest UA enforcement: omit `[FieldDefinition]` on `UserAgentOverride` — Settings UI shows no override field; ToS compliance mandatory (Pitfall 4 / SOURCE-07).
- Per-`SourceKey` escalation: shared with Plan 03-03 `IIndexerSourceStatusService` — failure pressure on one MangaDex instance disables ALL instances sharing `SourceKey="mangadex"`.
- `ReleaseInfo.DownloadUrl = https://api.mangadex.org/at-home/server/{chapterId}` — chapter MANIFEST URL (NOT a single image); Phase 4 dereferences to enumerate per-page image URLs.

## Manga Adaptation Notes

- This is the v1 BEDROCK source — every other Phase 3 source plugin (comix.to in Plan 03-05; MangaFire deferred to v2 per D-19) follows the same shape.
- Phase 8 cleanup: `MetadataSource/MangaDex/` and `Indexers/MangaDex/` collapse into a single tree; `MangaDexApi` + `MangaDexIndexerApi` merge.

## Cross-References

- Sonarr analog (closest, the single-source HttpIndexerBase plugin shape this was modelled on): `NzbDrone.Core/Indexers/Nyaa/Nyaa.cs` — DELETED in the Phase 3 TV-indexer removal; cited for provenance only, absent at HEAD (path shown repo-relative-from-`src/` since it no longer resolves). The live single-source analog at HEAD is the sibling `src/NzbDrone.Core/Indexers/Comix/` plugin.
- Phase 1 base: `src/NzbDrone.Core/Indexers/Http/HttpAggregatorBase.cs` (rate budget + honest UA)
- Phase 2 sibling: `src/NzbDrone.Core/MetadataSource/MangaDex/CLAUDE.md` (shared SourceKey + UA-hidden pattern)
- Plan 03-02 contract: manga overloads on IIndexer + ReleaseInfo extension
- Plan 03-03 D-17: per-SourceKey escalation service (`IIndexerSourceStatusService`)
- Plan 03-06 governance: `THIRD-PARTY-NOTICES.md` MangaDex section + `DIVERGENCE.md` Phase 3 entries
- API docs: <https://api.mangadex.org/docs>
- Rate limits: <https://api.mangadex.org/docs/2-limitations/>
- ToS: 40 req/min on `/at-home/server`; honest UA mandatory; scanlation-group attribution.

## Threats Mitigated

| Threat ID | Mitigation |
|-----------|------------|
| T-CONFIG-DRIFT-01 | `UserAgentOverride` has NO `[FieldDefinition]` — Settings UI cannot expose the override; reflection fixture enforces. |
| T-DOS-01 | `RateSeconds=1.5` default (40 req/min); SourceKey-keyed budget shared with Phase 2 metadata source; Phase 4 image-fetch shares same bucket. |
| T-INJ-02 | `decimal.TryParse(InvariantCulture)` on stringly-typed chapter numbers; no exception path. |
| T-INJ-URL | URLs built via string interpolation against URL-encoded GUIDs (`{id:D}` GUID format) — no user-input concat in URL. |
| T-PHASE-4-MANIFEST-DRIFT | `ReleaseInfo.DownloadUrl` is the chapter manifest URL (NOT a single image); contract documented here + in Plan 03-02 Pitfall 3. |
| T-NULL-DEREF | Null-safe access throughout `MangaDexParser` (`?.`, fallback `"Unknown"` mangaTitle, `"und"` lang); empty fields handled gracefully. |
