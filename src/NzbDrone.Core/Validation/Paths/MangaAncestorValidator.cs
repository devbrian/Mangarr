using System.Linq;
using FluentValidation.Validators;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Manga;

namespace NzbDrone.Core.Validation.Paths
{
    // Mirrors NzbDrone.Core.Validation.Paths.SeriesAncestorValidator (TV) per Phase 8 audit
    // gap-single (no-sibling/SeriesAncestorValidator.md). Rejects a manga whose Path is an
    // ancestor of an existing manga's Path — prevents nested library entries.
    //
    // AddMangaValidator wiring is deferred to a follow-up plan once cluster-05 path-shape
    // peers (MangaPathValidator etc.) land alongside this.
    public class MangaAncestorValidator : PropertyValidator
    {
        private readonly IMangaService _mangaService;

        public MangaAncestorValidator(IMangaService mangaService)
        {
            _mangaService = mangaService;
        }

        protected override string GetDefaultMessageTemplate() => "Path '{path}' is an ancestor of an existing manga";

        protected override bool IsValid(PropertyValidatorContext context)
        {
            if (context.PropertyValue == null)
            {
                return true;
            }

            context.MessageFormatter.AppendArgument("path", context.PropertyValue.ToString());

            return !_mangaService.GetAllMangaPaths().Any(s => context.PropertyValue.ToString().IsParentPath(s.Value));
        }
    }
}
