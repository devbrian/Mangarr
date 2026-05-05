using FluentValidation.Validators;
using NzbDrone.Core.Profiles.Translations;

namespace NzbDrone.Core.Validation
{
    public class TranslationProfileExistsValidator : PropertyValidator
    {
        private readonly ITranslationProfileService _translationProfileService;

        public TranslationProfileExistsValidator(ITranslationProfileService translationProfileService)
        {
            _translationProfileService = translationProfileService;
        }

        protected override string GetDefaultMessageTemplate() => "Translation Profile does not exist";

        protected override bool IsValid(PropertyValidatorContext context)
        {
            if (context?.PropertyValue == null || (int)context.PropertyValue == 0)
            {
                return true;
            }

            return _translationProfileService.Exists((int)context.PropertyValue);
        }
    }
}
