using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.AutoTagging.Specifications
{
    // Phase 24 v1.1 Wave 3 — manga-NEW spec (D-04 enum match). Sonarr has no peer;
    // backed by MangaDemographic enum sentinel (Shonen / Shojo / Seinen / Josei).
    // Manga.Demographic is nullable enum (MangaDemographic?); null -> no match
    // (nullable guard).
    public class DemographicSpecificationValidator : AbstractValidator<DemographicSpecification>
    {
        public DemographicSpecificationValidator()
        {
            // Value is an int-backed enum selection; 0 is non-meaningful (MangaDemographic
            // values start at 1 per Phase 24 D-04 convention).
            RuleFor(c => c.Value).GreaterThan(0);
        }
    }

    public class DemographicSpecification : AutoTaggingSpecificationBase
    {
        private static readonly DemographicSpecificationValidator Validator = new();

        public override int Order => 1;
        public override string ImplementationName => "Demographic";

        [FieldDefinition(1, Label = "AutoTaggingSpecificationDemographic", Type = FieldType.Select, SelectOptions = typeof(MangaDemographic))]
        public int Value { get; set; }

        protected override bool IsSatisfiedByWithoutNegate(Manga.Manga manga)
        {
            return manga.Demographic.HasValue && (int)manga.Demographic.Value == Value;
        }

        public override NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
