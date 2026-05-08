# NzbDrone.Core/CustomFormats

## Purpose

**Custom Formats** are user-defined release-scoring rules. Each format is a set of conditions (specifications) that, if all matched, gives a release a configurable score. Quality profiles can require minimum scores, prefer specific formats, etc.

The pattern is the **same Specification pattern** used by DecisionEngine — but scoped to a format (not the entire grab decision).

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\CustomFormats\`

## Top-Level Files

| File | Purpose |
|------|---------|
| `CustomFormat.cs` | Aggregate: name, includeCustomFormatWhenRenaming, list of specifications |
| `ICustomFormatRepository.cs` / `CustomFormatRepository.cs` | DB persistence |
| `ICustomFormatService.cs` / `CustomFormatService.cs` | CRUD |
| `CustomFormatCalculationService.cs` | Score a release/file against all custom formats |
| `ParsedCustomFormatScore.cs` | Result DTO |
| `SpecificationMatchesGroup.cs` | Group container for spec matching with AND/OR semantics |
| `ICustomFormatSpecification.cs` | Specification interface |
| `Events/` | `CustomFormatAddedEvent`, `CustomFormatUpdatedEvent`, `CustomFormatDeletedEvent` |

## Subdirectory: `Specifications/`

The available **format-condition primitives** users compose:

| Specification | Matches |
|---------------|---------|
| `ReleaseTitleSpecification` | Regex on release title |
| `LanguageSpecification` | Specific language present |
| `ReleaseGroupSpecification` | Regex on release group |
| `IndexerFlagSpecification` | Indexer flag (e.g. Internal) |
| `ResolutionSpecification` | Resolution (e.g., 1080p) |
| `SourceSpecification` | Source (e.g., WebRip, BluRay) |
| `SizeSpecification` | File size in range |
| `QualityModifierSpecification` | Real / Repack / Proper / etc. |
| `MultipleLanguageSpecification` | Multiple languages |
| `SubtitleLanguageSpecification` (?) | Subtitle language present |

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
| `LanguageSpecification` | Reusable as-is (English vs Japanese vs Korean) |
| `ReleaseGroupSpecification` | Maps to "Scanlation Group" |
| `IndexerFlagSpecification` | Reusable |

New manga-specific specs to add:
- `ColorVsBwSpecification` — color manga distinguishing from B&W
- `PageCountSpecification` — minimum/maximum page count
- `OfficialVsFanTranslationSpecification` — official/scanlation/fan
- `MagazineSpecification` — preferred magazine source (Shounen Jump, etc.)

## Cross-References

- [../CLAUDE.md](../CLAUDE.md) — NzbDrone.Core overview
- [../DecisionEngine/CLAUDE.md](../DecisionEngine/CLAUDE.md) — Same Specification pattern; consumes score
- [../Profiles/CLAUDE.md](../Profiles/CLAUDE.md) — Quality profiles consume custom format minimums
- [../Parser/CLAUDE.md](../Parser/CLAUDE.md) — Provides parsed metadata used by specs
