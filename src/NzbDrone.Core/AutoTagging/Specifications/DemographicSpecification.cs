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
            // Reject any int that doesn't correspond to a defined MangaDemographic
            // enum member (1..4). A pure `> 0` check let unbounded ints through that
            // would silently never match at evaluation time (line 34 casts straight
            // to (int)manga.Demographic.Value == Value).
            RuleFor(c => c.Value)
                .Must(v => System.Enum.IsDefined(typeof(MangaDemographic), v))
                .WithMessage("Value must be a valid MangaDemographic.");
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
