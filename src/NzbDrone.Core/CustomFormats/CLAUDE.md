# NzbDrone.Core/CustomFormats

## Purpose

**Custom Formats** are user-defined release-scoring rules. Each format is a set of conditions (specifications) that, if all matched, gives a release a configurable score. Quality profiles can require minimum scores, prefer specific formats, etc.

The pattern is the **same Specification pattern** used by DecisionEngine — but scoped to a format (not the entire grab decision).


## Top-Level Files

| File | Purpose |
|------|---------|
| `CustomFormat.cs` | Aggregate: name, includeCustomFormatWhenRenaming, list of specifications |
| `CustomFormatRepository.cs` | DB persistence (`ICustomFormatRepository` declared in-file) |
| `CustomFormatService.cs` | CRUD (`ICustomFormatService` declared in-file) |
| `CustomFormatCalculationService.cs` | Score a release/file against all custom formats |
| `CustomFormatInput.cs` / `MangaCustomFormatInput.cs` | Scoring-input DTO + manga-shaped subclass (adds `ChapterInfo`, `Manga`, `Release`, `SourceKey`; `ScanlationGroup` + `TranslatedLanguage` ride on `Release`) |
| `MediaType.cs` | `AppliesTo` discriminator enum — `All = 0` (reusable specs) / `Manga = 2` (manga-only specs) |
| `SpecificationMatchesGroup.cs` | Group container for spec matching with AND/OR semantics |
| `Events/` | `CustomFormatAddedEvent`, `CustomFormatUpdatedEvent`, `CustomFormatDeletedEvent` |

(`ICustomFormatSpecification.cs` lives under `Specifications/`, not at top level.)

## Subdirectory: `Specifications/`

The available **format-condition primitives** users compose. The TV-specific specs (Resolution / Source / QualityModifier / Language / ReleaseType) were trimmed during the manga fork — Migration `006_v1_2_scrub_dead_language_specification_from_custom_formats` scrubbed the last dead language spec. What remains:

| Specification | `AppliesTo` | Matches |
|---------------|-------------|---------|
| `ReleaseTitleSpecification` | All | Regex on release title |
| `ReleaseGroupSpecification` | All | Regex on release group |
| `IndexerFlagSpecification` | All | Indexer flag (e.g. Internal) |
| `SizeSpecification` | All | File size in range |
| `Manga/` subdir (4 specs) | Manga | `TranslatedLanguageSpecification`, `ScanlationGroupSpecification`, `SourceKeySpecification`, `ChapterTypeSpecification` — see [Specifications/Manga/CLAUDE.md](./Specifications/Manga/CLAUDE.md) |

Shared base classes: `CustomFormatSpecificationBase`, `RegexSpecificationBase`, `ICustomFormatSpecification`.

## How Scoring Works

For a given release:
1. For each `CustomFormat` configured:
   - For each `Specification` in the format:
     - Evaluate against the release
   - All specs in a group must match (AND); groups combine (OR or AND depending on config)
2. If the format matches, add `format.Score` to the running total
3. Final `CustomFormatScore` is attached to the `RemoteEpisode`

The score is then consumed by:
- `CustomFormatAllowedByProfileSpecification` (decision engine — must meet minimum)
- `DownloadDecisionComparer` (sort approved decisions — higher score wins)
- Quality profile UI (configure preferences)

## Adding a New Specification

1. Create `Specifications/MyNewSpec.cs` implementing `ICustomFormatSpecification`.
2. Define settings (e.g., `Value`, `Negate`).
3. Specs auto-discovered. Add UI form schema via attributes on the spec class.
4. Tests under `NzbDrone.Core.Test/CustomFormats/`.

## Manga Adaptation Notes

The custom-format **architecture is fully reusable**. Specifications need adaptation:

| Mangarr Spec | Manga Equivalent |
|-------------|-------------------|
| `ResolutionSpecification` (480p / 720p / 1080p) | DPI tier or "high quality" / "low quality" / "raw" |
| `SourceSpecification` (BluRay / WebRip) | Source ("Official" / "Scan" / "Raw" / "Magazine") |
| (TV-region language spec deleted Phase 32 — CORR-04) | Manga peer is `TranslatedLanguageSpecification` (BCP-47) at `Specifications/Manga/` |
| `ReleaseGroupSpecification` | Maps to "Scanlation Group" |
| `IndexerFlagSpecification` | Reusable |

The manga-specific specs that actually shipped (Phase 5 D-09, under `Specifications/Manga/`): `TranslatedLanguageSpecification`, `ScanlationGroupSpecification`, `SourceKeySpecification`, `ChapterTypeSpecification`.

## Cross-References

- [../CLAUDE.md](../CLAUDE.md) — NzbDrone.Core overview
- [../DecisionEngine/CLAUDE.md](../DecisionEngine/CLAUDE.md) — Same Specification pattern; consumes score
- [../Profiles/CLAUDE.md](../Profiles/CLAUDE.md) — Quality profiles consume custom format minimums
- [../Parser/CLAUDE.md](../Parser/CLAUDE.md) — Provides parsed metadata used by specs
