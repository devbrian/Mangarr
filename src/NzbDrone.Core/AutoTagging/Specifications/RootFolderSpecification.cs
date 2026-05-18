using FluentValidation;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;

namespace NzbDrone.Core.AutoTagging.Specifications
{
    // Phase 24 v1.1 Wave 3 — Sonarr-port verbatim restore from 6f857ba0e^
    // (Pattern 1 — Series.RootFolderPath -> Manga.RootFolderPath, same field name).
    public class RootFolderSpecificationValidator : AbstractValidator<RootFolderSpecification>
    {
        public RootFolderSpecificationValidator()
        {
            RuleFor(c => c.Value).IsValidPath();
        }
    }

    public class RootFolderSpecification : AutoTaggingSpecificationBase
    {
        private static readonly RootFolderSpecificationValidator Validator = new RootFolderSpecificationValidator();

        public override int Order => 1;
        public override string ImplementationName => "Root Folder";

        [FieldDefinition(1, Label = "AutoTaggingSpecificationRootFolder", Type = FieldType.RootFolder)]
        public string Value { get; set; }

        protected override bool IsSatisfiedByWithoutNegate(Manga.Manga manga)
        {
            return manga.RootFolderPath.PathEquals(Value);
        }

        public override NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
