using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Metadata.ComicInfo
{
    // Phase 30 Plan 30-04 D-04 — single-toggle UX. ZERO Annotations.FieldDefinition
    // attributes means SchemaBuilder reflection produces an empty Fields[] array on
    // the V5 resource — the Settings/Metadata Edit modal shows only the enable /
    // disable toggle the substrate provides; no per-provider advanced settings UI
    // in v1.2.
    //
    // v1.3+ adds the rich-Settings shape (tag filter, per-format options) when a
    // 2nd writer ships (Kodi NFO / Komga series.json / Kavita-flavored).
    //
    // Always-valid Validate() — no fields to validate. Mirrors the minimal-config
    // shape of KomgaNotificationSettings (which has fields but the always-valid
    // base-case is the same pattern).
    public class ComicInfoMetadataSettings : IProviderConfig
    {
        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult();
        }
    }
}
