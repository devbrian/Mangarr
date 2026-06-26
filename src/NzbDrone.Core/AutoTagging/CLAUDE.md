# AutoTagging

## Purpose

AutoTagging subsystem — rule-based automatic tagging of `Manga` entities by matching their metadata against user-authored specifications. Each `AutoTag` rule carries a `List<IAutoTaggingSpecification>` (combined OR-within-type + AND-across-types semantics) plus a target tag set; when ALL specs match a manga (per the Sonarr-canonical algorithm), the rule's tags are added to the manga. The Sonarr-canonical `RemoveTagsAutomatically` flag is preserved per Phase 24 D-05 (opt-in per rule; default false = additive; opt-in true removes ONLY the tags THAT rule itself applied when the rule stops matching).

**Restored in Phase 24** from `git show 6f857ba0e^:src/NzbDrone.Core/AutoTagging/` after Phase 15 Plan 15-10 DELETED the subtree ("TV-only feature; v1.x manga rebuild option"). The Sonarr-canonical `AutoTaggingService.GetTagChanges` algorithm + `SpecificationMatchesGroup.DidMatch` body + NEGATE handling on `AutoTagSpecificationBase` ship verbatim with mechanical `Series → Manga` substitution (Pitfall 3 anti-rewrite gate enforced by `SpecificationMatchesGroupFixture.source_file_contains_verbatim_didmatch_expression`).


## Key Files

### Root Files (8 — verbatim restore from `6f857ba0e^` + 1 NEW applier)

| File | Purpose |
|------|---------|
| `AutoTag.cs` | Domain entity (`ModelBase`). `{ string Name, List<IAutoTaggingSpecification> Specifications, bool RemoveTagsAutomatically, HashSet<int> Tags }`. Persists to `AutoTagging` table (defined in `001_mangarr_baseline.cs:303-307`; UNCHANGED by Phase 24 — table existed pre-delete; only the C# code was stripped). |
| `AutoTaggingService.cs` | `IAutoTaggingService` interface + `AutoTaggingService` impl. CRUD (`Insert/Update/Delete/All/GetById/AllForTag`) + `GetTagChanges(Manga manga)` — the core spec-evaluation method (Sonarr-canonical algorithm preserved byte-for-byte with `Series → Manga` substitution). Publishes `AutoTagsUpdatedEvent` on every mutation via `IEventAggregator`. Constructor: `(IAutoTaggingRepository, IRootFolderService, IEventAggregator, ICacheManager)`. |
| `AutoTaggingRepository.cs` | `BasicRepository<AutoTag>` extension. Inherits Polly retry pipeline from base (Phase 1 D-15). |
| `AutoTaggingChanges.cs` | DTO carrying `TagsToAdd` + `TagsToRemove` sets — the diff `GetTagChanges` returns to callers. |
| `AutoTagsUpdatedEvent.cs` | `IEvent` published on every CRUD mutation. Consumed by `TagController.IHandle<AutoTagsUpdatedEvent>` (re-wired Plan 24-04 AT-08 — broadcasts SignalR `tag` resource invalidation) AND by `MangaAutoTaggingApplier.IHandle<AutoTagsUpdatedEvent>` (Plan 24-03 — triggers retroactive full-library re-eval on rule save per D-06). |
| `SpecificationMatchesGroup.cs` | Internal helper grouping specs by type for OR-within-type / AND-across-types semantics. The `DidMatch` method body is the **Pitfall 3 anti-rewrite gate** — preserved verbatim from `6f857ba0e^` with whitespace-normalized literal-text fixture pin. |
| `MangaAutoTaggingApplier.cs` | **NEW Phase 24 Plan 24-03**. Standalone applier — `IHandle<AutoTagsUpdatedEvent>` + `IHandle<MangaAddedEvent>` + `IHandle<MangaRefreshCompleteEvent>` (D-06). Constructor: `(IAutoTaggingService, IMangaService, Logger)`. On `AutoTagsUpdatedEvent` (rule save), iterates `_mangaService.GetAllManga()` and applies `GetTagChanges` per-rule via `IMangaService.UpdateManga(manga, publishUpdatedEvent: false)`. On `MangaAddedEvent` + `MangaRefreshCompleteEvent`, per-manga single-update path. Sonarr V5 has NO standalone applier (tag-application flows through `ApplyTagsCommand` in upstream); Mangarr's D-06 picks the IHandle-based applier path. DryIoc auto-discovered via the `RegisterMany` convention scan — no manual registration. |

### Specifications/ (11 files — 8 ports + 3 manga-NEW per AT-03 + AT-04)

| File | Purpose |
|------|---------|
| `Specifications/IAutoTagSpecification.cs` | Provider plugin contract — `Order / Name / Implementation / ImplementationName / Negate / Required / IsSatisfiedBy(Manga)` (signature substituted from `IsSatisfiedBy(Series)` upstream). |
| `Specifications/AutoTagSpecificationBase.cs` | Abstract base. `IsSatisfiedBy` wraps `IsSatisfiedByWithoutNegate` with XOR-against-Negate template method — the NEGATE flag is inherited FREE by every spec impl. `Required` flag (per-spec opt-in to AND-required semantics) handled in the base. |
| `Specifications/GenreSpecification.cs` | Port. Matches `manga.Genres` against `IEnumerable<string> Value` (case-insensitive any-match). |
| `Specifications/YearSpecification.cs` | Port. Matches `manga.PublicationYear` against `[Min, Max]` range. |
| `Specifications/MonitoredSpecification.cs` | Port. Matches `manga.Monitored == (Value == 1)`. |
| `Specifications/StatusSpecification.cs` | Port (Pitfall 1 reshape — Mangarr's `MangaStatus` enum NEW Option A sentinel mirror at `Manga/MangaStatus.cs`, not Sonarr's string-on-entity shape). |
| `Specifications/RootFolderSpecification.cs` | Port. Matches `manga.RootFolderPath` against `string Value` (OrdinalIgnoreCase). |
| `Specifications/TranslationProfileSpecification.cs` | AT-03 split-half. Matches `manga.TranslationProfileId == Value`. Replaces Sonarr's `QualityProfileSpec` half-1. |
| `Specifications/CustomFormatProfileSpecification.cs` | AT-03 split-half. Matches `manga.CustomFormatProfileId == Value`. Replaces Sonarr's `QualityProfileSpec` half-2. |
| `Specifications/TagSpecification.cs` | Port. `FieldType.SeriesTag` enum value SURVIVED Phase 15 (Open Q #2 verified at `src/NzbDrone.Core/Annotations/FieldDefinitionAttribute.cs:104`); used verbatim — enum spelling is a Sonarr-fork-heritage breadcrumb. |
| `Specifications/AuthorArtistSpecification.cs` | Manga-NEW (AT-04 / D-03). Single combined spec matching `manga.PrimaryAuthor` OR `manga.Artist` against `IEnumerable<string> Value` (case-insensitive contains-any). NEGATE works on the combined match. |
| `Specifications/DemographicSpecification.cs` | Manga-NEW (AT-04 / D-04). Matches `manga.Demographic` (the new `MangaDemographic` enum — Shonen/Shojo/Seinen/Josei) against `IEnumerable<int> Value`. Backed by Migration 002 `Demographic` column. |
| `Specifications/ContentRatingSpecification.cs` | Manga-NEW (AT-04 — Option A sentinel mirror per 24-PLAN-DECISIONS row 11). Matches `manga.ContentRating` (string on entity; pre-Phase-24 field) against `MangaContentRating` enum (Safe/Suggestive/Erotica/Pornographic) via a string→enum helper at evaluation time. |

### 3 Sonarr-shape specs DROPPED (AT-05 + Open Q #1)

- **`NetworkSpecification.cs`** — TV-only. Manga has no `Network` field. See DIVERGENCE.md "Phase 24 — AutoTagging Restore-Rebuild" Entry 1.
- **`SeriesTypeSpecification.cs`** — TV-only. Mangarr's `MangaType` enum is presentation-only, not a filter axis in v1.1.
- **`OriginalCountrySpecification.cs`** — TV-only. Manga's country-of-origin is implicit in `MangaType` (Manhwa=KR / Manhua=CN / Manga=JP).
- **`OriginalLanguageSpecification.cs`** — Open Q #1 resolution. Manga's language axis lives on `ChapterFile.TranslatedLanguage` (Phase 16.1 canonical pattern), NOT on the `Manga` entity. `Manga.cs` has NO `OriginalLanguage` field. See DIVERGENCE.md Entry 3.

**Spec total = 11** (pinned in `AutoTaggingSpecificationCatalogFixture.Should().HaveCount(11)` + `AutoTaggingDryIocAutoDiscoveryFixture.resolved_count_is_11`). Derivation: 8 Sonarr ports − 1 OriginalLanguage drop + 1 QualityProfile split-add + 3 manga-NEW = 11; the +1 split-add and −1 drop cancel.

## Patterns / Conventions

- **NEGATE flag inherited from `AutoTagSpecificationBase`** — every spec impl gets the negate behavior for free via the template-method base class. No per-spec NEGATE code needed.
- **OR-within-type / AND-across-types / AND-with-Required semantics** — `SpecificationMatchesGroup.DidMatch` groups specs by type (multiple Genre rules in one AutoTag rule OR together; specs of different types AND together; `Required=true` specs override the OR-grouping to AND with the rest). Sonarr-canonical — preserved byte-for-byte.
- **DryIoc auto-discovery** — `IAutoTaggingSpecification` impls + `MangaAutoTaggingApplier` (3 IHandle interfaces) are convention-scanned via the `RegisterMany` registration in `Mangarr.Common`. No manual DI registration for new specs or appliers.
- **Events published via `IEventAggregator`** — `AutoTaggingService.Insert/Update/Delete` each publish `AutoTagsUpdatedEvent` (Sonarr-canonical; preserved verbatim). Consumers: `TagController.IHandle<AutoTagsUpdatedEvent>` (broadcasts SignalR `tag` sync) + `MangaAutoTaggingApplier.IHandle<AutoTagsUpdatedEvent>` (retroactive full-library eval per D-06).
- **`UpdateManga(manga, publishUpdatedEvent: false)`** during bulk re-tag — applier passes `false` to suppress per-manga update flood (the `AutoTagsUpdatedEvent` already fired; TagController re-broadcasts SignalR for FE tag-list invalidation). Phase 10 Plan 10-07 invariant maintained: DB Update FIRST, `MangaUpdatedEvent` SECOND (when flag true).
- **`[FieldDefinition]` schema reflection** — every spec declares `[FieldDefinition(N, Label = "AutoTaggingSpecification<X>", Type = FieldType.<Y>)]` on its `Value` property. `Mangarr.Http.ClientSchema.SchemaBuilder.ToSchema(model)` reflects on these to build the FE schema rows served by `[HttpGet("schema")]` on the V5 controller. No bespoke serialization code for AutoTagging.

## Manga Adaptation Notes

### Inherited from Sonarr (verbatim)

- Sonarr-canonical `GetTagChanges` algorithm. `RemoveTagsAutomatically` flag (per D-05 reversal of the original user-additive lock — Mangarr matches Sonarr verbatim here, no DIVERGENCE entry needed for the precedence model).
- `SpecificationMatchesGroup.DidMatch` body (Pitfall 3 anti-rewrite gate).
- NEGATE handling on `AutoTagSpecificationBase`.
- `IEventAggregator` event-train shape (Insert/Update/Delete → `AutoTagsUpdatedEvent`).
- `IProvider` + auto-discovery pattern.

### Diverged from Sonarr

- **`QualityProfileSpec` SPLIT into 2** (`TranslationProfileSpec` + `CustomFormatProfileSpec`) per AT-03 — Phase 5 D-04 decomposed manga's quality axis across two profile types.
- **3 specs DROPPED** per AT-05 (Network/SeriesType/OriginalCountry) — no manga peer fields.
- **OriginalLanguage DROPPED** per Open Q #1 — manga's language axis lives on `ChapterFile.TranslatedLanguage` per Phase 16.1 canonical pattern (NOT on the `Manga` entity).
- **3 manga-NEW specs ADDED** per AT-04 — `AuthorArtist` (combined PrimaryAuthor OR Artist), `Demographic` (Shonen/Shojo/Seinen/Josei enum), `ContentRating` (Safe/Suggestive/Erotica/Pornographic enum). Backed by Migration 002's `Manga.Artist` (NEW TEXT NULL) + `Manga.Demographic` (NEW INTEGER NULL — `MangaDemographic` enum).
- **`MangaAutoTaggingApplier` standalone class** — Sonarr V5 has NO standalone applier (its tag application uses `ApplyTagsCommand`); Mangarr's D-06 picks the IHandle-based path for cleaner event-trigger semantics (rule-save + manga-added + refresh-complete each evaluate the rules independently).
- **AniList + MAL `MapManga` fall-back** — Phase 24 ships MangaDex `MapManga` population of `Artist` + `Demographic` only. AniList + MAL leave those fields null; AutoTagging on those axes just doesn't fire for AniList/MAL-only manga until Phase 27 wires the field paths.

## Cross-References

- [../Manga/CLAUDE.md](../Manga/CLAUDE.md) — `Manga` entity (`Artist` + `Demographic` field additions per Migration 002).
- [../Datastore/CLAUDE.md](../Datastore/CLAUDE.md) — Migration 002 (`002_v1_1_manga_artist_demographic.cs`).
- [../../Mangarr.Api.V5/CLAUDE.md](../../Mangarr.Api.V5/CLAUDE.md) — V5 `AutoTaggingController` (CRUD + inlined `[HttpGet("schema")]`) at `Mangarr.Api.V5/AutoTagging/`.
- [../Validation/](../Validation/) — `TagInUseValidator.cs` Slot 8 (AutoTagging) — Phase 22 D-03 stub-flip closed Plan 24-04.
- [../Tags/](../Tags/) — `TagController.IHandle<AutoTagsUpdatedEvent>` re-wire (AT-08) at `Mangarr.Api.V5/Tags/TagController.cs`.
- [../MetadataSource/MangaDex/CLAUDE.md](../MetadataSource/MangaDex/CLAUDE.md) — `MapManga` population of `Artist` + `Demographic` + Open Q #6 `includes[]=artist` query-param add on `MangaDexApi.cs`.
- `.planning/phases/24-autotagging-restore-rebuild-v1-1-inserted-2026-05-17/` — phase decisions D-01..D-07 + 6 Open Q resolutions + 11-spec catalog plan map + AT-07 amendment text.
- `DIVERGENCE.md` § Phase 24 — AutoTagging Restore-Rebuild (2026-05-17) — 3 entries documenting spec catalog divergence + MangaDemographic enum source + OriginalLanguageSpec drop.

---
*Created: 2026-05-18 (Phase 24 Plan 24-05 Task 2 — AutoTagging subtree CLAUDE.md authored at phase close per CLAUDE.md HIGH PRIORITY documentation maintenance directive. Mirrors Manga/CLAUDE.md shape; covers the 8 root files + 11 specs + applier + the 4 dropped specs + Sonarr divergence narrative).*
