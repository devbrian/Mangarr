# Profiles/CustomFormats

## Purpose
Custom-Format scoring profile entity (CustomFormatProfile) — the INNER scoring layer of Phase 5's two-layer release-preference model. Carries `MinFormatScore` / `MaxFormatScore` thresholds + a per-profile per-CF score override list (`FormatItems`).

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Profiles\CustomFormats`

## Key Files
| File | Purpose |
|------|---------|
| `CustomFormatProfile.cs` | ModelBase entity: Name + MinFormatScore + MaxFormatScore (NULLABLE) + FormatItems : List<ProfileFormatItem> |
| `CustomFormatProfileRepository.cs` | BasicRepository<CustomFormatProfile> (no overrides — preserves Phase 1 D-15 Polly retry envelope) |
| `CustomFormatProfileService.cs` | CRUD + IHandle<ApplicationStartedEvent> + IHandle<CustomFormatAddedEvent> + IHandle<CustomFormatDeletedEvent> + InUseException-on-Delete guard |
| `CustomFormatProfileInUseException.cs` | Raised on Delete when assigned to any Manga or as global default |
| `CustomFormatProfileUpdatedEvent.cs` | Published on Update for cache invalidation |

## Patterns / Conventions
- Sibling to `Profiles/Qualities/` AND to `Profiles/Translations/` — Phase 5 D-07 orthogonal-concerns split (language preference on TranslationProfile, CF scoring on CustomFormatProfile)
- FormatItems persists as JSON column via `EmbeddedDocumentConverter<List<ProfileFormatItem>>` already registered at `TableMapping.cs:213`
- Default-on-first-run seeder per pattern S4 + Open Question 2: `Add({Name="Default", MinFormatScore=0, MaxFormatScore=null, FormatItems=ICustomFormatService.All().Select(f => new ProfileFormatItem{Score=0, Format=f})})` then seed `Config.DefaultCustomFormatProfileId`
- `Handle(CustomFormatAddedEvent)` auto-inserts new CF at score=0 into every profile (mirrors `QualityProfileService.cs:159-173`)
- `Handle(CustomFormatDeletedEvent)` auto-removes the CF from every profile (mirrors `QualityProfileService.cs:175-191`)
- Delete guard per Pitfall 8: raises CustomFormatProfileInUseException if profile is assigned to any Manga OR is the global default

## Manga Adaptation Notes
This is a NEW manga-side directory split from Mangarr's QualityProfile per Phase 5 D-07. Three KEY divergences (per Phase 5 PATTERNS-MAP Adaptation Hotspots 4 + 9):

1. `MaxFormatScore : int?` is **NULLABLE** (Mangarr QualityProfile has no max-score concept; null = no cap)
2. `FormatItems` shipped per Open Question 2 (power users want per-profile per-CF score override)
3. **Drops** UpgradeAllowed/Cutoff/Items/MinUpgradeFormatScore/CutoffFormatScore (TV-quality-specific)
4. **Drops** Languages list (lives on TranslationProfile per D-07 orthogonal split)

Phase 5 D-04: REQUIREMENTS.md CF-03 reword deferred to Wave 4 plan 05-07 — adds language: "Min/max score live on `CustomFormatProfile` entity (per Phase 5 D-07); per-Manga FK + global default."

## Phase 8 Collapse
When `Tv/` deletes in Phase 8, this directory collapses into the canonical `Profiles/` namespace. The collapse is non-trivial because Mangarr's QualityProfile may also need a parallel collapse — DIVERGENCE.md (Wave 4 plan 05-07) tracks the intent.

## Cross-References
- [Phase 5 CONTEXT](../../../.planning/phases/05-decision-engine-translationprofile-custom-formats-naming/05-CONTEXT.md) — D-07, D-11
- [Phase 5 RESEARCH](../../../.planning/phases/05-decision-engine-translationprofile-custom-formats-naming/05-RESEARCH.md) — Open Question 2 (FormatItems shipping), Pitfall 8 (FK orphan)
- [Phase 5 PATTERNS-MAP](../../../.planning/phases/05-decision-engine-translationprofile-custom-formats-naming/05-PATTERNS.md) — Adaptation Hotspots 4 + 9
- [QualityProfile pattern source](../Qualities/QualityProfile.cs)
- [TranslationProfile sibling](../Translations/) (Wave 1 plan 05-02)
- [DIVERGENCE.md](../../../../DIVERGENCE.md) — Phase 5 D-07 entry (added in Wave 4 plan 05-07)
