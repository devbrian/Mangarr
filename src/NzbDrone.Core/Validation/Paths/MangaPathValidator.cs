using System.Linq;
using FluentValidation.Validators;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Manga;

namespace NzbDrone.Core.Validation.Paths
{
    // Mirrors NzbDrone.Core.Validation.Paths.SeriesPathValidator (TV) per Phase 8 audit
    // gap-single (no-sibling/SeriesPathValidator.md). Rejects a manga whose Path equals
    // an existing manga's Path — prevents duplicate library entries pointing at the same
    // folder.
    //
    // AddMangaValidator wiring is deferred to a follow-up plan once cluster-05 path-shape
    // peers (MangaAncestorValidator etc.) land alongside this.
    public class MangaPathValidator : PropertyValidator
    {
        private readonly IMangaService _mangaService;

        public MangaPathValidator(IMangaService mangaService)
        {
            _mangaService = mangaService;
        }

        protected override string GetDefaultMessageTemplate() => "Path '{path}' is already configured for another manga";

        protected override bool IsValid(PropertyValidatorContext context)
        {
            if (context.PropertyValue == null)
            {
                return true;
            }

            context.MessageFormatter.AppendArgument("path", context.PropertyValue.ToString());

            dynamic instance = context.ParentContext.InstanceToValidate;
            var instanceId = (int)instance.Id;

            // Skip the path for this manga and any invalid paths
            return !_mangaService.GetAllMangaPaths().Any(s => s.Key != instanceId &&
                                                              s.Value.IsPathValid(PathValidationType.CurrentOs) &&
                                                              s.Value.PathEquals(context.PropertyValue.ToString()));
        }
    }
}
