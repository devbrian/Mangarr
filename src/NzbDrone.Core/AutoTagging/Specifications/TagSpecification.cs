using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.AutoTagging.Specifications
{
    // Phase 24 v1.1 Wave 3 — Sonarr-port verbatim restore from 6f857ba0e^
    // (Pattern 1 — Series.Tags -> Manga.Tags, both HashSet<int>). FieldType.SeriesTag
    // preserved verbatim per 24-PLAN-DECISIONS Open Q #2 resolution -- the enum value
    // survives at FieldDefinitionAttribute.cs:104 and is a Sonarr-fork-heritage
    // breadcrumb (FE TagSelectInput keys off the enum value, not its spelling).
    public class TagSpecificationValidator : AbstractValidator<TagSpecification>
    {
        public TagSpecificationValidator()
        {
            RuleFor(c => c.Value).GreaterThan(0);
        }
    }

    public class TagSpecification : AutoTaggingSpecificationBase
    {
        private static readonly TagSpecificationValidator Validator = new();

        public override int Order => 1;
        public override string ImplementationName => "Tag";

        [FieldDefinition(1, Label = "AutoTaggingSpecificationTag", Type = FieldType.SeriesTag)]
        public int Value { get; set; }

        protected override bool IsSatisfiedByWithoutNegate(Manga.Manga manga)
        {
            return manga.Tags.Contains(Value);
        }

        public override NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
