using FluentValidation;

namespace Sonarr.Api.V5.Manga;

// Sonarr divergence: NEW manga V5 bulk-edit validator per Phase 13 Plan 13-04.
// Mirrors src/Sonarr.Api.V5/Series/SeriesEditorValidator.cs structurally so the
// MangaEditorController constructor can inject it the same way SeriesEditorController
// injects SeriesEditorValidator — preserves the shape verbatim for an easier
// Phase 15 collapse.
//
// EMPTY ruleset for v1: the frontend MangaEditorModal already validates the inputs
// and the backend IMangaService.UpdateManga/DeleteManga reject illegal field values
// at the persistence boundary. RootFolder + per-profile validation can grow here
// later (TV's analog ships RootFolderExistsValidator + QualityProfileExistsValidator
// — manga's Phase 5 + Phase 7 ship per-Manga TranslationProfile/CustomFormatProfile
// FK validation at the AddManga boundary, not at the bulk-edit boundary).
//
// Phase 15 collapse target: merge with SeriesEditorValidator into a domain-neutral
// BulkEditValidator after the Phase 8 Series→Manga rename completes.
public class MangaEditorValidator : AbstractValidator<NzbDrone.Core.Manga.Manga>
{
    public MangaEditorValidator()
    {
        // Intentionally empty — see class comment.
    }
}
