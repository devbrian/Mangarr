using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.AutoTagging.Specifications
{
    // Phase 24 v1.1 Wave 3 — Sonarr-port verbatim restore from 6f857ba0e^
    // (Pattern 1 — Series -> Manga mechanical translation). Body unchanged: manga.Genres
    // is List<string> with the same shape as Sonarr's series.Genres.
    public class GenreSpecificationValidator : AbstractValidator<GenreSpecification>
    {
        public GenreSpecificationValidator()
        {
            RuleFor(c => c.Value).NotEmpty();
        }
    }

    public class GenreSpecification : AutoTaggingSpecificationBase
    {
        private static readonly GenreSpecificationValidator Validator = new();

        public override int Order => 1;
        public override string ImplementationName => "Genre";

        [FieldDefinition(1, Label = "AutoTaggingSpecificationGenre", Type = FieldType.Tag)]
        public IEnumerable<string> Value { get; set; }

        protected override bool IsSatisfiedByWithoutNegate(Manga.Manga manga)
        {
            return manga.Genres.Any(genre => Value.ContainsIgnoreCase(genre));
        }

        public override NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
