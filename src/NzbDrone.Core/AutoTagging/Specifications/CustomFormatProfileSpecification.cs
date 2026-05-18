using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.AutoTagging.Specifications
{
    // Phase 24 v1.1 Wave 3 — AT-03 split-add. Sonarr's single QualityProfileSpecification
    // splits into TWO specs in Mangarr because the manga's quality axis splits across
    // two profile types (TranslationProfile + CustomFormatProfile per Phase 5 D-01 + D-07).
    // This file is the CUSTOM-FORMAT half: compares Manga.CustomFormatProfileId
    // against the rule's Value.
    //
    // FieldType: uses QualityProfile fallback per 24-PLAN-DECISIONS Open Q resolution
    // (line 399) -- a dedicated FieldType.CustomFormatProfile enum value is heavier
    // than the FE-side ImplementationName discrimination. FE picker reads the
    // ImplementationName "Custom Format Profile" to pick the correct profile-dropdown
    // variant (lands in 24-04).
    public class CustomFormatProfileSpecificationValidator : AbstractValidator<CustomFormatProfileSpecification>
    {
        public CustomFormatProfileSpecificationValidator()
        {
            RuleFor(c => c.Value).GreaterThan(0);
        }
    }

    public class CustomFormatProfileSpecification : AutoTaggingSpecificationBase
    {
        private static readonly CustomFormatProfileSpecificationValidator Validator = new();

        public override int Order => 1;
        public override string ImplementationName => "Custom Format Profile";

        [FieldDefinition(1, Label = "AutoTaggingSpecificationCustomFormatProfile", Type = FieldType.QualityProfile)]
        public int Value { get; set; }

        protected override bool IsSatisfiedByWithoutNegate(Manga.Manga manga)
        {
            return manga.CustomFormatProfileId.HasValue && Value == manga.CustomFormatProfileId.Value;
        }

        public override NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
