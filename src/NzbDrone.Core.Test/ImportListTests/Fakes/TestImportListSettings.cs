using NzbDrone.Core.Annotations;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Test.ImportListTests.Fakes
{
    // Phase 26 Plan 26-04 (D-09 + Pitfall 2) — test-infrastructure ONLY. Lives in
    // NzbDrone.Core.Test; the production ProviderFactory reflection-scan iterates
    // NzbDrone.Core types only and never sees this assembly. NO DIVERGENCE.md entry
    // per D-11 (test-only, not production behavior).
    //
    // Minimal IImportListSettings implementation — no real provider knobs, just enough
    // shape that TestImportList can declare a TSettings type parameter and
    // NullConfig-equivalent round-trip behavior in unit fixtures.
    public class TestImportListSettings : IImportListSettings
    {
        [FieldDefinition(0, Label = "Base URL")]
        public string BaseUrl { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult();
        }
    }
}
