using System;
using FluentValidation.Validators;
using NzbDrone.Core.Manga;

namespace NzbDrone.Core.Validation.Paths
{
    // Mirrors NzbDrone.Core.Validation.Paths.SeriesExistsValidator (TV) per Phase 8 audit
    // gap-single (no-sibling/SeriesExistsValidator.md). Rejects an Add-Manga request when
    // a Manga with the same source ID already exists in DB.
    //
    // Manga has THREE source IDs (MangaDex GUID / AniList int / MAL int) per D-15. The
    // validator inspects the property value at runtime: Guid → MangaDex lookup, int →
    // attempts both AniList and MAL (consumer wires this to a specific field, so in
    // practice only one match path is meaningful per wire-site).
    //
    // AddMangaValidator + V5 controller wiring is deferred to a follow-up plan.
    public class MangaExistsValidator : PropertyValidator
    {
        private readonly IMangaService _mangaService;

        public MangaExistsValidator(IMangaService mangaService)
        {
            _mangaService = mangaService;
        }

        protected override string GetDefaultMessageTemplate() => "This manga has already been added";

        protected override bool IsValid(PropertyValidatorContext context)
        {
            if (context.PropertyValue == null)
            {
                return true;
            }

            if (context.PropertyValue is Guid mangaDexId)
            {
                return _mangaService.FindByMangaDexId(mangaDexId) == null;
            }

            if (Guid.TryParse(context.PropertyValue.ToString(), out var parsedGuid))
            {
                return _mangaService.FindByMangaDexId(parsedGuid) == null;
            }

            var intId = Convert.ToInt32(context.PropertyValue.ToString());

            return _mangaService.FindByAniListId(intId) == null
                && _mangaService.FindByMalId(intId) == null;
        }
    }
}
