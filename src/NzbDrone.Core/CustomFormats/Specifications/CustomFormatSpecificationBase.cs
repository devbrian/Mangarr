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
            // quick-260608-gmm: the WR-03 AppliesTo cross-bleed guard (AppliesTo == MediaType.Series
            // && input is MangaCustomFormatInput → return false) was removed along with the Series
            // media type. With zero Series specs the guard is unreachable. Manga specs keep their own
            // `is not MangaCustomFormatInput → return false` casts, and All-type specs
            // (ReleaseTitle/Size/ReleaseGroup/IndexerFlag) correctly evaluate against manga inputs.
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
