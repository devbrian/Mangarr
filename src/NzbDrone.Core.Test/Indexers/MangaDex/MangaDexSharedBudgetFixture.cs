using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers.MangaDex;
using NzbDrone.Core.MetadataSource.MangaDex;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.MangaDex
{
    /// <summary>
    /// Wave 0 fixture realizing Pitfall 5 mitigation by integration test:
    /// the Phase 2 metadata source and the Phase 3 indexer MUST share the same
    /// <c>SourceKey == "mangadex"</c> so they share a single rate budget at the
    /// <c>IRateLimitService</c> bucket level.
    ///
    /// References NOT-YET-BUILT type <see cref="MangaDexIndexerSettings"/> (lands in Plan 03-04);
    /// references already-shipped <see cref="MangaDexMetadataSourceSettings"/> from Phase 2.
    /// </summary>
    [TestFixture]
    public class MangaDexSharedBudgetFixture : CoreTest
    {
        [Test]
        public void Indexer_and_metadata_source_settings_share_SourceKey_mangadex()
        {
            var indexerSettings = new MangaDexIndexerSettings();
            var metadataSettings = new MangaDexMetadataSourceSettings();

            indexerSettings.SourceKey.Should().Be("mangadex");
            metadataSettings.SourceKey.Should().Be("mangadex");
            indexerSettings.SourceKey.Should().Be(
                metadataSettings.SourceKey,
                "Phase 1 D-11/D-12 + Phase 2 D-22 + Phase 3 D-17 — single shared rate budget per "
                + "SourceKey value across metadata source + indexer.");
        }
    }
}
