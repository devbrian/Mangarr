using FluentValidation;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;

namespace Mangarr.Api.V5.Manga;

// Sonarr divergence: NEW manga V5 bulk-edit validator per Phase 13 Plan 13-04 +
// Plan 13-13 CR-03 closure (T-13-03 mass-assignment closure).
// Mirrors src/Mangarr.Api.V5/Series/SeriesEditorValidator.cs structurally so the
// MangaEditorController constructor can inject it the same way SeriesEditorController
// injects SeriesEditorValidator — preserves the shape verbatim for an easier
// Phase 15 collapse.
//
// Phase 13 Plan 13-13 CR-03 closure (T-13-03 mass-assignment mitigation completion):
// The Plan 13-04 ship landed an EMPTY ruleset on the assumption that
// IMangaService.UpdateManga rejected illegal field values at the persistence boundary.
// 13-REVIEW.md CR-03 cross-checked MangaService.UpdateManga(List<Manga>, bool) and
// confirmed there is NO FK-existence check or root-folder validation in that path —
// the rows are upserted as-is. Submitting a PUT body with rootFolderPath:
// "C:\Windows\System32" or translationProfileId: 999999 would silently mass-assign
// these values across every row in mangaIds, defeating the T-13-03 mitigation that
// was attached to the bulk-edit boundary.
//
// This validator now ships:
//   * RootFolderPath rule mirroring SeriesEditorValidator.cs:12-15 — IsValidPath()
//     + RootFolderExistsValidator. Gated by .When(m => m.RootFolderPath.IsNotNullOrWhiteSpace())
//     so that omitted/empty path strings (the no-op "no change" case) skip validation.
//   * TranslationProfileId rule — SetValidator(TranslationProfileExistsValidator).
//     Manga.TranslationProfileId is int? (nullable, fall back to
//     Config.DefaultTranslationProfileId). The existing validator's IsValid contract
//     short-circuits to true when PropertyValue is null OR (int)PropertyValue == 0,
//     so it safely handles the null/zero "no change" case. No .When() gate needed —
//     the validator's internal null-handling does the job.
//
// Skipped (CustomFormatProfileId): the manga side has NO
// CustomFormatProfileExistsValidator peer in v1 (verified by Grep across
// src/NzbDrone.Core/Validation/). The CustomFormatProfileService exists but no
// per-Manga FK existence validator was authored alongside it. Documenting the gap
// inline; v1.1 roadmap entry should backfill the validator and extend this class
// with the matching SetValidator rule.
//
// Phase 15 collapse target: merge with SeriesEditorValidator into a domain-neutral
// BulkEditValidator after the Phase 8 Series→Manga rename completes.
public class MangaEditorValidator : AbstractValidator<NzbDrone.Core.Manga.Manga>
{
    public MangaEditorValidator(RootFolderExistsValidator rootFolderExistsValidator,
                                TranslationProfileExistsValidator translationProfileExistsValidator)
    {
        RuleFor(m => m.RootFolderPath).Cascade(CascadeMode.Stop)
            .IsValidPath()
            .SetValidator(rootFolderExistsValidator)
            .When(m => m.RootFolderPath.IsNotNullOrWhiteSpace());

        // Manga.TranslationProfileId is int? — TranslationProfileExistsValidator.IsValid
        // short-circuits to true when PropertyValue is null OR cast-to-int == 0, so a row
        // whose TranslationProfileId is left at null/0 (the "no change" / fallback case)
        // passes through without a database hit. Non-zero ids are checked against
        // ITranslationProfileService.Exists.
        RuleFor(m => m.TranslationProfileId).SetValidator(translationProfileExistsValidator);

        // CustomFormatProfileId existence check intentionally omitted — no
        // CustomFormatProfileExistsValidator peer exists on the manga side (v1 gap; v1.1
        // roadmap). Adding the rule here without the validator would NotResolveDep at
        // DryIoc resolution time and break controller instantiation.
    }
}
