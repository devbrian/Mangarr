using System;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.CustomFormats
{
    public class TranslatedLanguageSpecificationValidator : AbstractValidator<TranslatedLanguageSpecification>
    {
        public TranslatedLanguageSpecificationValidator()
        {
            RuleFor(c => c.Value).Custom((value, context) =>
            {
                if (string.IsNullOrWhiteSpace(value) || IsoLanguages.Find(value) == null)
                {
                    context.AddFailure($"Invalid BCP-47 translation language code: {value}");
                }
            });
        }
    }

    // Sonarr divergence: BCP-47 string field instead of Sonarr Language enum (Adaptation Hotspot 2).
    // Distinct from LanguageSpecification (which uses Sonarr's TV-region Language enum and applies
    // to MediaType.Series). This sibling spec pairs with TranslationProfile (BCP-47 string list).
    // See DIVERGENCE.md per Phase 5 D-09.
    //
    // BCP-47 validation uses NzbDrone.Core.Parser.IsoLanguages.Find(code) — Find returns non-null
    // for valid 2-letter / 3-letter / 2-letter-COUNTRY shapes (e.g., en, eng, pt-br) per plan
    // 05-02 SUMMARY (IsoLanguages.IsBcp47Valid does NOT exist in this codebase).
    //
    // Phase 8 collapse: drops AppliesTo discriminator + namespace path collapse into canonical
    // CustomFormats/Specifications/ when Tv/ deletes.
    public class TranslatedLanguageSpecification : CustomFormatSpecificationBase
    {
        private static readonly TranslatedLanguageSpecificationValidator Validator = new TranslatedLanguageSpecificationValidator();

        public override int Order => 11;
        public override string ImplementationName => "Translated Language";
        public override MediaType AppliesTo => MediaType.Manga;     // Phase 5 D-10

        [FieldDefinition(1, Label = "BCP-47 Language Code", HelpText = "e.g., en, es, ja, ko")]
        public string Value { get; set; }

        protected override bool IsSatisfiedByWithoutNegate(CustomFormatInput input)
        {
            // Pitfall 4 cast — sibling MangaCustomFormatInput per Phase 5 D-09.
            // Wave 0 chose derived class (option a) so this cast is type-safe at compile-time.
            if (input is not MangaCustomFormatInput mangaInput)
            {
                return false;
            }

            return string.Equals(mangaInput.Release?.TranslatedLanguage, Value, StringComparison.OrdinalIgnoreCase);
        }

        public override NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
