using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Metadata.Stax
{
    // quick-260701-e71 — single-toggle UX. Mirrors ComicInfoMetadataSettings verbatim in
    // shape: ZERO Annotations.FieldDefinition attributes means SchemaBuilder reflection
    // produces an empty Fields[] array on the V5 resource — the Settings/Metadata Edit
    // modal shows only the enable / disable toggle the substrate provides; no per-provider
    // advanced settings UI for Stax.
    //
    // Always-valid Validate() — no fields to validate.
    public class StaxMetadataSettings : IProviderConfig
    {
        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult();
        }
    }
}
