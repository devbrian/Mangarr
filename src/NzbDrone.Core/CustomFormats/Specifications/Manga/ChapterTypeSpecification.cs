using NzbDrone.Core.Annotations;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.CustomFormats
{
    // Wave 2 RED stub — body lands in GREEN commit. Phase 5 D-09 / D-10.
    public class ChapterTypeSpecification : CustomFormatSpecificationBase
    {
        public override int Order => 13;
        public override string ImplementationName => "Chapter Type";
        public override MediaType AppliesTo => MediaType.Manga;

        [FieldDefinition(1, Label = "Chapter Type", Type = FieldType.Select, SelectOptions = typeof(ChapterType))]
        public int Value { get; set; }

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
