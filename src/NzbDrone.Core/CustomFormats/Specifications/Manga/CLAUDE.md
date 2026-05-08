# CustomFormats/Specifications/Manga

## Purpose
4 NEW manga-specific Custom Format spec classes per Phase 5 D-09. Auto-discovered via DryIoc by virtue of implementing `ICustomFormatSpecification` (no manual DI registration). Distinct from Mangarr's TV CF specs (Resolution, Source, ReleaseType, Language) which are hidden from manga CF authoring UI via `AppliesTo == MediaType.Series`.

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\CustomFormats\Specifications\Manga`

## Key Files
| File | Purpose |
|------|---------|
| `TranslatedLanguageSpecification.cs` | BCP-47 string match against `MangaCustomFormatInput.Release.TranslatedLanguage` (NOT Mangarr's Language enum) |
| `ScanlationGroupSpecification.cs` | Regex match against `MangaCustomFormatInput.Release.ScanlationGroup` (Phase 3 indexer-populated) |
| `SourceKeySpecification.cs` | Regex match against `MangaCustomFormatInput.SourceKey` (Phase 3 D-17) |
| `ChapterTypeSpecification.cs` | Enum-select match against `MangaCustomFormatInput.ChapterInfo.ChapterType` (Phase 2 D-09) |

## Patterns / Conventions
- All 4 specs declare `AppliesTo => MediaType.Manga` per Phase 5 D-10 discriminator
- All 4 specs cast `input is not MangaCustomFormatInput → return false` per Pitfall 4 + the Wave 0 sibling-input choice (MangaCustomFormatInput derives from CustomFormatInput, so the cast is type-safe)
- TranslatedLanguageSpecification uses BCP-47 string Value (Adaptation Hotspot 2 — distinct from Mangarr's int-keyed Language enum). BCP-47 validation uses `IsoLanguages.Find(code) != null` (the canonical Mangarr API; `IsoLanguages.IsBcp47Valid` does NOT exist in this codebase per plan 05-02 SUMMARY)
- ScanlationGroup + SourceKey extend `RegexSpecificationBase` — they inherit `Regex.Compiled | RegexOptions.IgnoreCase` and the null-safe `MatchString(compared)` helper. T-05-19 (catastrophic-backtracking DoS) mitigation surface lives on the base class
- ChapterType uses `[FieldDefinition(... Type = FieldType.Select, SelectOptions = typeof(ChapterType))]` pattern (mirror of `LanguageSpecification.cs:30`); enum lives at `NzbDrone.Core.Parser.Manga.ChapterType`

## Override hook
The base `CustomFormatSpecificationBase` provides the `IsSatisfiedBy(CustomFormatInput)` entry-point with negate handling and exposes `protected abstract bool IsSatisfiedByWithoutNegate(CustomFormatInput)` for derived classes to implement. All 4 specs override `IsSatisfiedByWithoutNegate` (NOT the public `IsSatisfiedBy`).

## Manga Adaptation Notes
These specs are auto-discovered AND auto-flow through Mangarr's existing CF JSON round-trip (per CF-04) — `CustomFormatResource.MapSpecification` reflects on `Implementation` field via `GetType().Name` (verified at `Mangarr.Api.V5/CustomFormats/CustomFormatResource.cs:55`). Wave 4 plan 05-07's `MangaCustomFormatRoundTripFixture` asserts lossless round-trip across all four NEW spec types.

## Phase 8 Collapse
When `Tv/` deletes in Phase 8, this directory collapses into the canonical `CustomFormats/Specifications/` namespace (the existing TV-shaped specs delete in the same cutover; only manga remains). The `MediaType.AppliesTo` discriminator and the `CustomFormatController?mediaType=manga` query filter both retire at the same time.

## Cross-References
- [Phase 5 CONTEXT](../../../../.planning/phases/05-decision-engine-translationprofile-custom-formats-naming/05-CONTEXT.md) — D-09, D-10
- [Phase 5 RESEARCH](../../../../.planning/phases/05-decision-engine-translationprofile-custom-formats-naming/05-RESEARCH.md) — Example 3 (TranslatedLanguageSpecification full impl); Pitfall 4 (sibling input class)
- [Phase 5 PATTERNS-MAP](../../../../.planning/phases/05-decision-engine-translationprofile-custom-formats-naming/05-PATTERNS.md) — Adaptation Hotspot 2 (BCP-47 not Language enum)
- [MangaCustomFormatInput sibling DTO](../../MangaCustomFormatInput.cs)
- [CustomFormatSpecificationBase](../CustomFormatSpecificationBase.cs)
- [RegexSpecificationBase](../RegexSpecificationBase.cs)
