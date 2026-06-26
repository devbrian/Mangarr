# Profiles/Translations

## Purpose
Manga translation-language preference entity (TranslationProfile) — the OUTER gate of Phase 5's two-layer release-preference model (Phase 0 cf-only-walkthrough.md verdict signed off 2026-05-01).


## Key Files
| File | Purpose |
|------|---------|
| `TranslationProfile.cs` | ModelBase entity: Name + Languages : List<string> + AllowLanguagesNotInProfile : bool |
| `TranslationProfileRepository.cs` | BasicRepository<TranslationProfile> (no overrides — preserves Phase 1 D-15 Polly retry envelope) |
| `TranslationProfileService.cs` | CRUD + IHandle<ApplicationStartedEvent> seeder + InUseException-on-Delete guard |
| `TranslationProfileInUseException.cs` | Raised on Delete when assigned to any Manga or as global default |
| `TranslationProfileUpdatedEvent.cs` | Published on Update for cache invalidation |

## Patterns / Conventions
- Sibling to `Profiles/CustomFormats/` — the two halves of the Phase 5 D-07 split. The shape was ported verbatim from Sonarr's QualityProfile{,Service,Repository,InUseException} (per 05-PATTERNS.md exact-match analogs); that Sonarr `Profiles/Qualities/` vertical was dropped in Phase 5 D-04 and no longer exists on disk.
- Languages persists as JSON column via `StringListConverter<List<string>>` already registered at `TableMapping.cs:233` (Phase 5 PATTERNS-MAP Adaptation Hotspot 8 — no new converter needed)
- Default-on-first-run seeder per pattern S4: `if (All().Any()) return;` then `Add(new TranslationProfile { Name="English Only", Languages=["en"], AllowLanguagesNotInProfile=false })` and seed `Config.DefaultTranslationProfileId` (per Phase 5 D-11 first-run-UX dependency + Pitfall 8 mitigation)
- AllowLanguagesNotInProfile defaults to FALSE per Phase 5 D-02 (strict mode — vast majority of users have one language; user direction 2026-05-03)
- Delete guard per Pitfall 8: raises TranslationProfileInUseException if profile is assigned to any Manga OR is the global default (Phase 5 port of the since-deleted Sonarr `QualityProfileService.Delete` guard). Manga lookup uses `IMangaService.GetAllManga()`.
- BCP-47 validation in V5 controller uses `NzbDrone.Core.Parser.IsoLanguages.Find(code) != null` rather than the planned `IsBcp47Valid` (which does not exist in this codebase). `Find` accepts 2-letter, 3-letter, and 2-letter-COUNTRY shapes (e.g., `en`, `eng`, `pt-br`).

## Manga Adaptation Notes
This is a NEW manga-side directory with no TV analog (Mangarr has no TranslationProfile equivalent). The shape mirrors QualityProfile (sibling profile entity), but the semantics are NEW:
- Languages : List<string> (BCP-47 ordered list; index = preference rank) instead of Items : List<QualityProfileQualityItem>
- AllowLanguagesNotInProfile : bool fallback control instead of UpgradeAllowed/Cutoff
- No FormatItems (CF score thresholds live on the SEPARATE `Profiles/CustomFormats/CustomFormatProfile` entity per Phase 5 D-07 — orthogonal user concerns)

Phase 5 D-04: REQUIREMENTS.md LANG-03 ("user can specify preferred languages per Manga; global default also configurable") collapses into TPROFILE-02 — Phase 5 ships TranslationProfile as the single mechanism (small REQUIREMENTS.md reword deferred to Wave 4 plan 05-07).

## Phase 8 Collapse
When `Tv/` deletes in Phase 8, this directory collapses into the canonical `Profiles/` namespace alongside the renamed QualityProfile-equivalent. The DIVERGENCE.md entry for Phase 5 documents the parallel-hierarchy intent.

## Cross-References
- [Phase 5 CONTEXT](../../../.planning/phases/05-decision-engine-translationprofile-custom-formats-naming/05-CONTEXT.md) — D-01, D-02, D-03, D-04, D-11
- [Phase 5 RESEARCH](../../../.planning/phases/05-decision-engine-translationprofile-custom-formats-naming/05-RESEARCH.md) — Pitfall 8 (FK orphan via REST DELETE)
- [Phase 5 PATTERNS-MAP](../../../.planning/phases/05-decision-engine-translationprofile-custom-formats-naming/05-PATTERNS.md) — Adaptation Hotspot 8 (StringListConverter<List<string>> already registered)
- [CustomFormatProfile sibling](../CustomFormats/) — the other half of the Phase 5 D-07 split (Sonarr's QualityProfile pattern source was dropped Phase 5 D-04)
- [DIVERGENCE.md](../../../../DIVERGENCE.md) — Phase 5 D-01 entry (added in Wave 4 plan 05-07)
