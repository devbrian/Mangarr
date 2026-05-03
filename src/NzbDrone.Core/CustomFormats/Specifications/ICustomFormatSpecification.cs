using NzbDrone.Core.Validation;

namespace NzbDrone.Core.CustomFormats
{
    public interface ICustomFormatSpecification
    {
        int Order { get; }
        string InfoLink { get; }
        string ImplementationName { get; }
        string Name { get; set; }
        bool Negate { get; set; }
        bool Required { get; set; }
        MediaType AppliesTo { get; }              // Phase 5 D-10 — Phase 8 cleanup: drop when TV specs delete

        NzbDroneValidationResult Validate();

        ICustomFormatSpecification Clone();
        bool IsSatisfiedBy(CustomFormatInput input);
    }
}
