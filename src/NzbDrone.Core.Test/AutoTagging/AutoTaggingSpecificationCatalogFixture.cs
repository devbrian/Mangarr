using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.AutoTagging.Specifications;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.AutoTagging
{
    // Phase 24 v1.1 Wave 3 — catalog fixture pinning the total spec count at 11.
    //
    // Derivation (per 24-PLAN-DECISIONS.md Spec Catalog Plan Map):
    //   8 Sonarr ports - 1 OriginalLanguage drop (Open Q #1) + 1 QualityProfile
    //   split-add (AT-03; 1->2) + 3 manga-NEW (AuthorArtist + Demographic +
    //   ContentRating) = 11. Net = 11 -- the +1 split-add and -1 OriginalLanguage
    //   drop cancel, hence pinning is consistent with CONTEXT.md D-03 + ROADMAP
    //   Phase 24 Success Criteria #2.
    //
    // This fixture asserts via reflection scan of the Mangarr.Core assembly --
    // does NOT boot a DryIoc container (auto-discovery contract is verified
    // separately in AutoTaggingDryIocAutoDiscoveryFixture). The literal `11` is
    // pinned directly (not re-derived) so a stale value here is a loud failure.
    [TestFixture]
    public class AutoTaggingSpecificationCatalogFixture : CoreTest
    {
        private static System.Type[] DiscoverConcreteSpecs()
        {
            return typeof(IAutoTaggingSpecification).Assembly
                .GetTypes()
                .Where(t => typeof(IAutoTaggingSpecification).IsAssignableFrom(t)
                            && !t.IsAbstract
                            && !t.IsInterface)
                .ToArray();
        }

        [Test]
        public void Catalog_contains_exactly_eleven_concrete_specs()
        {
            var specs = DiscoverConcreteSpecs();

            specs.Should().HaveCount(11,
                "Spec total = 11 pinned per 24-PLAN-DECISIONS.md Spec Catalog Plan Map");
        }

        [Test]
        public void Catalog_contains_eight_sonarr_port_specs()
        {
            var specs = DiscoverConcreteSpecs();

            specs.Should().Contain(t => t.Name == "GenreSpecification");
            specs.Should().Contain(t => t.Name == "YearSpecification");
            specs.Should().Contain(t => t.Name == "MonitoredSpecification");
            specs.Should().Contain(t => t.Name == "StatusSpecification");
            specs.Should().Contain(t => t.Name == "RootFolderSpecification");
            specs.Should().Contain(t => t.Name == "TranslationProfileSpecification");
            specs.Should().Contain(t => t.Name == "CustomFormatProfileSpecification");
            specs.Should().Contain(t => t.Name == "TagSpecification");
        }

        [Test]
        public void Catalog_contains_three_manga_new_specs()
        {
            var specs = DiscoverConcreteSpecs();

            specs.Should().Contain(t => t.Name == "AuthorArtistSpecification",
                "D-03 combined PrimaryAuthor OR Artist spec");
            specs.Should().Contain(t => t.Name == "DemographicSpecification",
                "D-04 MangaDemographic enum spec");
            specs.Should().Contain(t => t.Name == "ContentRatingSpecification",
                "MangaContentRating enum sentinel spec");
        }

        [Test]
        public void Catalog_does_not_contain_AT_05_dropped_specs()
        {
            // AT-05 dropped: TV-only specs with no manga peer fields.
            var specs = DiscoverConcreteSpecs();

            specs.Should().NotContain(t => t.Name == "NetworkSpecification");
            specs.Should().NotContain(t => t.Name == "SeriesTypeSpecification");
            specs.Should().NotContain(t => t.Name == "OriginalCountrySpecification");
        }

        [Test]
        public void Catalog_does_not_contain_OriginalLanguageSpecification()
        {
            // Open Q #1: dropped because manga's language axis lives on
            // ChapterFile.TranslatedLanguage per Phase 16.1 canonical pattern.
            var specs = DiscoverConcreteSpecs();

            specs.Should().NotContain(t => t.Name == "OriginalLanguageSpecification");
        }

        [Test]
        public void Every_concrete_spec_has_Order_one()
        {
            // Sonarr-canonical: Order = 1 uniformly. The ordering relationship is
            // across multiple specs WITHIN a rule (group-by-type OR-semantic + AND-
            // across-types per SpecificationMatchesGroup.DidMatch), NOT a global
            // per-class precedence.
            var specs = DiscoverConcreteSpecs();

            foreach (var type in specs)
            {
                var instance = (IAutoTaggingSpecification)System.Activator.CreateInstance(type);
                instance.Order.Should().Be(1, $"{type.Name} must report Order=1");
            }
        }
    }
}
