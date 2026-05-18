using System;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.AutoTagging.Specifications
{
    // Phase 24 v1.1 Wave 3 — manga-NEW spec (Option A sentinel — MangaContentRating
    // enum mirrors MangaDex 4-value: Safe / Suggestive / Erotica / Pornographic).
    // Same shape as StatusSpecification reshape: Manga.ContentRating is STRING on
    // the entity, the spec stores user int Value, evaluation maps int -> enum ->
    // ToString() -> case-insensitive equality against the stored string.
    public class ContentRatingSpecificationValidator : AbstractValidator<ContentRatingSpecification>
    {
        public ContentRatingSpecificationValidator()
        {
            // Reject any int that doesn't correspond to a defined MangaContentRating
            // enum member (1..4). A pure `> 0` check let unbounded ints through that
            // line 41 then casts to (MangaContentRating)Value — undefined values
            // produce empty .ToString() and silently never match.
            RuleFor(c => c.Value)
                .Must(v => Enum.IsDefined(typeof(MangaContentRating), v))
                .WithMessage("Value must be a valid MangaContentRating.");
        }
    }

    public class ContentRatingSpecification : AutoTaggingSpecificationBase
    {
        private static readonly ContentRatingSpecificationValidator Validator = new();

        public override int Order => 1;
        public override string ImplementationName => "Content Rating";

        [FieldDefinition(1, Label = "AutoTaggingSpecificationContentRating", Type = FieldType.Select, SelectOptions = typeof(MangaContentRating))]
        public int Value { get; set; }

        protected override bool IsSatisfiedByWithoutNegate(Manga.Manga manga)
        {
            if (string.IsNullOrWhiteSpace(manga.ContentRating))
            {
                return false;
            }

            return ((MangaContentRating)Value).ToString()
                .Equals(manga.ContentRating, StringComparison.OrdinalIgnoreCase);
        }

        public override NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
