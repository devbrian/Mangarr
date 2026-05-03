namespace NzbDrone.Core.CustomFormats
{
    // Wave 2 RED stub — body lands in GREEN commit. Phase 5 D-09 / D-10.
    public class ScanlationGroupSpecification : RegexSpecificationBase
    {
        public override int Order => 12;
        public override string ImplementationName => "Scanlation Group";
        public override MediaType AppliesTo => MediaType.Manga;

        protected override bool IsSatisfiedByWithoutNegate(CustomFormatInput input)
        {
            return false;
        }
    }
}
