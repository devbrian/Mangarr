using System.Linq;
using FluentValidation.Validators;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Manga;

namespace NzbDrone.Core.Validation.Paths
{
    // Mirrors NzbDrone.Core.Tv.SeriesTitleSlugValidator per Phase 8 audit gap-single
    // (no-sibling/SeriesTitleSlugValidator.md). Rejects a manga whose TitleSlug
    // matches an existing manga's TitleSlug (excluding self by Id) — prevents URL
    // collisions on the frontend /manga/:titleSlug route.
    //
    // Originally deferred (Plan 05-06 first pass) when Manga.TitleSlug didn't yet
    // exist. Plan 05-07 (commit 68a956771) shipped the property, unblocking this
    // backfill.
    //
    // AddMangaValidator wiring is deferred to a follow-up plan once the cluster-05
    // sibling-set is complete.
    public class MangaTitleSlugValidator : PropertyValidator
    {
        private readonly IMangaService _mangaService;

        public MangaTitleSlugValidator(IMangaService mangaService)
        {
            _mangaService = mangaService;
        }

        protected override string GetDefaultMessageTemplate() =>
            "Title slug '{slug}' is in use by manga '{mangaTitle}'. Check the FAQ for more information";

        protected override bool IsValid(PropertyValidatorContext context)
        {
            if (context.PropertyValue == null)
            {
                return true;
            }

            dynamic instance = context.ParentContext.InstanceToValidate;
            var instanceId = (int)instance.Id;
            var slug = context.PropertyValue.ToString();

            var conflictingManga = _mangaService.GetAllManga()
                                                .FirstOrDefault(m => m.TitleSlug.IsNotNullOrWhiteSpace() &&
                                                                     m.TitleSlug.Equals(slug) &&
                                                                     m.Id != instanceId);

            if (conflictingManga == null)
            {
                return true;
            }

            context.MessageFormatter.AppendArgument("slug", slug);
            context.MessageFormatter.AppendArgument("mangaTitle", conflictingManga.Title);

            return false;
        }
    }
}
