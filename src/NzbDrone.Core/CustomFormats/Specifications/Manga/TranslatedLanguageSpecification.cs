using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.CustomFormats
{
    // Wave 2 RED stub — body lands in GREEN commit. Phase 5 D-09 / D-10.
    public class TranslatedLanguageSpecification : CustomFormatSpecificationBase
    {
        public override int Order => 11;
        public override string ImplementationName => "Translated Language";
        public override MediaType AppliesTo => MediaType.Manga;

        [FieldDefinition(1, Label = "BCP-47 Language Code", HelpText = "e.g., en, es, ja, ko")]
        public string Value { get; set; }

        protected override bool IsSatisfiedByWithoutNegate(CustomFormatInput input)
        {
            return false;
        }

        public override NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult();
        }
    }
}
