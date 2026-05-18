using System.Collections.Generic;
using FluentValidation;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.AutoTagging.Specifications
{
    // Phase 24 v1.1 Wave 3 — manga-NEW spec (D-03 combined OR). Sonarr has no peer;
    // this is a domain-shape addition for manga (analogous to author/artist filtering
    // which TV doesn't expose). Matches either Manga.PrimaryAuthor OR Manga.Artist
    // case-insensitively against the rule's Value set. NEGATE flag works on the
    // combined OR as a unit per D-03.
    //
    // Field shape mirrors GenreSpecification (FieldType.Tag + IEnumerable<string> Value
    // + ContainsIgnoreCase). The Common StringExtensions ContainsIgnoreCase wraps
    // Enumerable.Contains with InvariantCultureIgnoreCase comparer; it tolerates null
    // input strings (Enumerable.Contains accepts null and uses the comparer).
    public class AuthorArtistSpecificationValidator : AbstractValidator<AuthorArtistSpecification>
    {
        public AuthorArtistSpecificationValidator()
        {
            RuleFor(c => c.Value).NotEmpty();
        }
    }

    public class AuthorArtistSpecification : AutoTaggingSpecificationBase
    {
        private static readonly AuthorArtistSpecificationValidator Validator = new();

        public override int Order => 1;
        public override string ImplementationName => "Author / Artist";

        [FieldDefinition(1, Label = "AutoTaggingSpecificationAuthorArtist", Type = FieldType.Tag)]
        public IEnumerable<string> Value { get; set; }

        protected override bool IsSatisfiedByWithoutNegate(Manga.Manga manga)
        {
            if (Value == null)
            {
                return false;
            }

            return Value.ContainsIgnoreCase(manga.PrimaryAuthor)
                || Value.ContainsIgnoreCase(manga.Artist);
        }

        public override NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
