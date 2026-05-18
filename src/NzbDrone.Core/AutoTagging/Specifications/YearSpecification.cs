using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.AutoTagging.Specifications
{
    // Phase 24 v1.1 Wave 3 — Sonarr-port verbatim restore from 6f857ba0e^
    // (Pattern 1 — Series.Year -> Manga.PublicationYear). PublicationYear is int? on
    // Manga (vs Sonarr's non-nullable int Year), so the body wraps the comparison with
    // a HasValue guard per PATTERNS lines 713-715. Null PublicationYear -> no match.
    public class YearSpecificationValidator : AbstractValidator<YearSpecification>
    {
        public YearSpecificationValidator()
        {
            RuleFor(c => c.Min).NotEmpty();
            RuleFor(c => c.Min).GreaterThan(0);
            RuleFor(c => c.Max).NotEmpty();
            RuleFor(c => c.Max).GreaterThanOrEqualTo(c => c.Min);
        }
    }

    public class YearSpecification : AutoTaggingSpecificationBase
    {
        private static readonly YearSpecificationValidator Validator = new();

        public override int Order => 1;
        public override string ImplementationName => "Year";

        [FieldDefinition(1, Label = "AutoTaggingSpecificationMinimumYear", Type = FieldType.Number)]
        public int Min { get; set; }

        [FieldDefinition(2, Label = "AutoTaggingSpecificationMaximumYear", Type = FieldType.Number)]
        public int Max { get; set; }

        protected override bool IsSatisfiedByWithoutNegate(Manga.Manga manga)
        {
            if (!manga.PublicationYear.HasValue)
            {
                return false;
            }

            return manga.PublicationYear.Value >= Min && manga.PublicationYear.Value <= Max;
        }

        public override NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
