using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.AutoTagging.Specifications
{
    // Phase 24 v1.1 Wave 3 — AT-03 split-add. Sonarr's single QualityProfileSpecification
    // splits into TWO specs in Mangarr because the manga's quality axis splits across
    // two profile types (TranslationProfile + CustomFormatProfile per Phase 5 D-01 + D-07).
    // This file is the TRANSLATION half: compares Manga.TranslationProfileId against
    // the rule's Value.
    //
    // FieldType: uses QualityProfile fallback per 24-PLAN-DECISIONS Open Q resolution
    // (line 399) -- a dedicated FieldType.TranslationProfile enum value is heavier than
    // the FE-side ImplementationName discrimination. FE picker reads the
    // ImplementationName "Translation Profile" to pick the correct profile-dropdown
    // variant (lands in 24-04).
    public class TranslationProfileSpecificationValidator : AbstractValidator<TranslationProfileSpecification>
    {
        public TranslationProfileSpecificationValidator()
        {
            RuleFor(c => c.Value).GreaterThan(0);
        }
    }

    public class TranslationProfileSpecification : AutoTaggingSpecificationBase
    {
        private static readonly TranslationProfileSpecificationValidator Validator = new();

        public override int Order => 1;
        public override string ImplementationName => "Translation Profile";

        [FieldDefinition(1, Label = "AutoTaggingSpecificationTranslationProfile", Type = FieldType.QualityProfile)]
        public int Value { get; set; }

        protected override bool IsSatisfiedByWithoutNegate(Manga.Manga manga)
        {
            return manga.TranslationProfileId.HasValue && Value == manga.TranslationProfileId.Value;
        }

        public override NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
