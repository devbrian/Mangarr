using NzbDrone.Core.Validation;

namespace NzbDrone.Core.CustomFormats
{
    public abstract class CustomFormatSpecificationBase : ICustomFormatSpecification
    {
        public abstract int Order { get; }
        public abstract string ImplementationName { get; }

        public virtual string InfoLink => "https://wiki.servarr.com/sonarr/settings#custom-formats-2";
        public virtual MediaType AppliesTo => MediaType.All;       // Phase 5 D-10 — default reusable; TV/manga specs override

        public string Name { get; set; }
        public bool Negate { get; set; }
        public bool Required { get; set; }

        public ICustomFormatSpecification Clone()
        {
            return (ICustomFormatSpecification)MemberwiseClone();
        }

        public abstract NzbDroneValidationResult Validate();

        public virtual bool IsSatisfiedBy(CustomFormatInput input)
        {
            // WR-03: AppliesTo cross-bleed guard. AppliesTo is honored at UI schema-list time
            // (CustomFormatController.GetTemplates?mediaType=...) but at score-calculation time
            // every spec is evaluated against every input — including the manga overload. The
            // manga-side specs already use an `is not MangaCustomFormatInput → return false`
            // cast guard to skip TV inputs; mirror the inverse here so MediaType.Series specs
            // (e.g. ResolutionSpecification, SourceSpecification, ReleaseTypeSpecification)
            // refuse to match a MangaCustomFormatInput rather than silently returning true on
            // Resolution.Unknown / QualitySource.Unknown / etc.
            //
            // Note: this short-circuits BEFORE Negate flips, so MediaType.Series + Negate=true
            // does NOT spuriously match every manga release.
            if (AppliesTo == MediaType.Series && input is MangaCustomFormatInput)
            {
                return false;
            }

            var match = IsSatisfiedByWithoutNegate(input);

            if (Negate)
            {
                match = !match;
            }

            return match;
        }

        protected abstract bool IsSatisfiedByWithoutNegate(CustomFormatInput input);
    }
}
