using NUnit.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MangaPipeline
{
    // Phase 6 Wave 0 — shared in-memory DI base for Phase 6 fixtures.
    // Wave 1+ fixtures inherit; Wave 5 F-01 fixture extends with full DI graph.
    //
    // Future Wave 1 implementation (per F-01 researcher pattern + 06-VALIDATION.md):
    //   * Spin up an in-memory DI container (DryIoc) with the real services
    //     under test plus any required fakes (Mocker pattern from CoreTest).
    //   * Seed: 1 Manga + 1 Chapter + 1 TranslationProfile + 1 CustomFormatProfile
    //     + minimal Config keys.
    //   * Provide accessors to seeded entities via protected properties.
    //
    // For Wave 0 this is a marker base class; inheritors compile but do not
    // instantiate the container (the BLOCKING fixture tests are [Ignore]'d).
    [TestFixture]
    public abstract class MangaPipelineTestBase : TestBase
    {
        // TODO Wave 1: implement Setup() with DB seed + DI container per F-01 researcher pattern.
        // TODO Wave 5: extend with full real-DI round-trip support for MangaPipelineEndToEndFixture.
    }
}
