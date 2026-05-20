using FluentValidation.Validators;
using NzbDrone.Core.ImportLists.Exclusions;

namespace Mangarr.Api.V5.ImportLists;

// Phase 26 Plan 26-05 (IL-05) — ported from
// .planning/reference/sonarr-vertical-slices/import-lists/v5-controller/ImportListExclusionExistsValidator.cs
// with the Sonarr `TvdbId == (int)PropertyValue` uniqueness check swapped for a
// MangaDexId string-equality check via the manga-ID triplet shape (Plan 26-04
// IImportListExclusionService.FindByMangaDexId).
//
// T-26-05-03 mitigation: NULL MangaDexId is allowed through (AniList-only or
// MAL-only exclusions ride a NULL MangaDexId per SQLite UNIQUE-with-NULLs
// semantics — Migration 003 §Q4). The validator only enforces uniqueness when
// the candidate property value is non-null.
public class ImportListExclusionExistsValidator : PropertyValidator
{
    private readonly IImportListExclusionService _importListExclusionService;

    public ImportListExclusionExistsValidator(IImportListExclusionService importListExclusionService)
    {
        _importListExclusionService = importListExclusionService;
    }

    protected override string GetDefaultMessageTemplate() => "This exclusion has already been added.";

    protected override bool IsValid(PropertyValidatorContext context)
    {
        if (context.PropertyValue == null)
        {
            return true;
        }

        if (context.InstanceToValidate is not ImportListExclusionResource listExclusionResource)
        {
            return true;
        }

        var mangaDexId = context.PropertyValue as string;
        if (string.IsNullOrWhiteSpace(mangaDexId))
        {
            // NULL / whitespace MangaDexId passes — secondary-ID-only exclusions
            // (AniList-only / MAL-only) can legitimately ride NULL MangaDexId
            // per Migration 003's UNIQUE-with-NULLs index semantics.
            return true;
        }

        return !_importListExclusionService.All().Exists(v => v.MangaDexId == mangaDexId && v.Id != listExclusionResource.Id);
    }
}
