using System;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.CustomFormats
{
    public class ChapterTypeSpecificationValidator : AbstractValidator<ChapterTypeSpecification>
    {
        public ChapterTypeSpecificationValidator()
        {
            RuleFor(c => c.Value).Custom((value, context) =>
            {
                if (!Enum.IsDefined(typeof(ChapterType), value))
                {
                    context.AddFailure($"Invalid ChapterType enum value: {value}");
                }
            });
        }
    }

    // Sonarr divergence: NEW enum-select spec per Phase 5 D-09 — see DIVERGENCE.md.
    // Reads ParsedChapterInfo.ChapterType enum (Phase 2 D-09: Regular / Extra / Bonus /
    // SideStory / Oneshot / Prologue / Epilogue / Special). Lets users filter or score
    // by chapter type (e.g., negative CF score on SideStory so they don't pollute the
    // main feed). Mirrors LanguageSpecification's enum-select shape (FieldType.Select +
    // typeof(ChapterType) generates the dropdown in Phase 7 UI).
    //
    // ChapterType enum lives at NzbDrone.Core.Parser.Manga (NOT NzbDrone.Core.Manga as
    // the plan's draft suggested) — verified location at src/NzbDrone.Core/Parser/Manga/
    // ChapterType.cs.
    //
    // Phase 8 collapse: drops AppliesTo discriminator when Tv/ deletes.
    public class ChapterTypeSpecification : CustomFormatSpecificationBase
    {
        private static readonly ChapterTypeSpecificationValidator Validator = new ChapterTypeSpecificationValidator();

        public override int Order => 13;
        public override string ImplementationName => "Chapter Type";
        public override MediaType AppliesTo => MediaType.Manga;     // Phase 5 D-10

        [FieldDefinition(1, Label = "Chapter Type", Type = FieldType.Select, SelectOptions = typeof(ChapterType))]
        public int Value { get; set; }

        protected override bool IsSatisfiedByWithoutNegate(CustomFormatInput input)
        {
            // Pitfall 4 cast — sibling MangaCustomFormatInput per Phase 5 D-09.
            if (input is not MangaCustomFormatInput mangaInput)
            {
                return false;
            }

            if (mangaInput.ChapterInfo == null)
            {
                return false;
            }

            return mangaInput.ChapterInfo.ChapterType == (ChapterType)Value;
        }

        public override NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
