using System.Linq;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.CustomFormats
{
    public class LanguageSpecificationValidator : AbstractValidator<LanguageSpecification>
    {
        public LanguageSpecificationValidator()
        {
            RuleFor(c => c.Value).Custom((value, context) =>
            {
                if (!Language.All.Any(o => o.Id == value))
                {
                    context.AddFailure(string.Format("Invalid Language condition value: {0}", value));
                }
            });
        }
    }

    public class LanguageSpecification : CustomFormatSpecificationBase
    {
        private static readonly LanguageSpecificationValidator Validator = new LanguageSpecificationValidator();

        public override int Order => 3;
        public override string ImplementationName => "Language";
        public override MediaType AppliesTo => MediaType.Series;   // Phase 5 D-10 — TV-only spec; hidden from manga CF UI per CustomFormatController?mediaType=manga filter

        [FieldDefinition(1, Label = "CustomFormatsSpecificationLanguage", Type = FieldType.Select, SelectOptions = typeof(LanguageFieldConverter))]
        public int Value { get; set; }

        [FieldDefinition(1, Label = "CustomFormatsSpecificationExceptLanguage", HelpText = "CustomFormatsSpecificationExceptLanguageHelpText", Type = FieldType.Checkbox)]
        public bool ExceptLanguage { get; set; }

        public override bool IsSatisfiedBy(CustomFormatInput input)
        {
            if (Negate)
            {
                return IsSatisfiedByWithNegate(input);
            }

            return IsSatisfiedByWithoutNegate(input);
        }

        // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — input.EpisodeInfo / input.Series
        // checks stripped per Plan 15-10 CustomFormatInput TV-shape DELETE. AppliesTo=Series above
        // hides the spec from manga CF UI; for v1.x this spec is dead-bound but compiles.
        // Original-language-fallback path no longer applies (no Series in input).
        protected override bool IsSatisfiedByWithoutNegate(CustomFormatInput input)
        {
            var comparedLanguage = (Language)Value;

            if (ExceptLanguage)
            {
                return input.Languages?.Any(l => l != comparedLanguage) ?? false;
            }

            return input.Languages?.Contains(comparedLanguage) ?? false;
        }

        private bool IsSatisfiedByWithNegate(CustomFormatInput input)
        {
            var comparedLanguage = (Language)Value;

            if (ExceptLanguage)
            {
                return !input.Languages?.Any(l => l != comparedLanguage) ?? false;
            }

            return !input.Languages?.Contains(comparedLanguage) ?? false;
        }

        public override NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
