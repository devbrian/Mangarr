using FluentValidation;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.AutoTagging.Specifications
{
    // Phase 24 v1.1 Wave 3 — Sonarr-port verbatim restore from 6f857ba0e^
    // (Pattern 1 — Series.Monitored -> Manga.Monitored, trivial 1:1 substitution).
    public class MonitoredSpecificationValidator : AbstractValidator<MonitoredSpecification>
    {
    }

    public class MonitoredSpecification : AutoTaggingSpecificationBase
    {
        private static readonly MonitoredSpecificationValidator Validator = new();

        public override int Order => 1;
        public override string ImplementationName => "Monitored";

        protected override bool IsSatisfiedByWithoutNegate(Manga.Manga manga)
        {
            return manga.Monitored;
        }

        public override NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
