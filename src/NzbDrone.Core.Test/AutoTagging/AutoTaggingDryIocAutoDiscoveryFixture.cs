using System.Linq;
using DryIoc;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.AutoTagging.Specifications;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.AutoTagging
{
    // Phase 24 v1.1 Wave 3 — DryIoc auto-discovery contract for IAutoTaggingSpecification.
    //
    // Asserts the production RegisterMany convention (NzbDrone.Common/Composition/
    // Extensions.cs:25-35) auto-discovers all 11 spec impls without explicit
    // registration. Pattern mirrored from
    // src/NzbDrone.Core.Test/Indexers/Comix/ComixSignerDryIocResolutionFixture.cs
    // (Phase 17 Wave 1 — same DryIoc auto-discovery contract verification).
    //
    // The pinned literal 11 matches AutoTaggingSpecificationCatalogFixture's
    // HaveCount(11) and 24-PLAN-DECISIONS.md Spec Catalog `Spec total = 11`.
    [TestFixture]
    public class AutoTaggingDryIocAutoDiscoveryFixture : CoreTest
    {
        private static IContainer BuildRealContainer()
        {
            // Mirror the production AutoAddServices RegisterMany convention from
            // NzbDrone.Common/Composition/Extensions.cs:25-35, but driven directly
            // off the already-loaded Mangarr.Core assembly to keep the fixture
            // hermetic (no IDatabase / SQLite chain).
            var rules = Rules.Default
                .WithMicrosoftDependencyInjectionRules()
                .WithAutoConcreteTypeResolution()
                .WithDefaultReuse(Reuse.Singleton);
            var container = new Container(rules);

            var assemblies = new[]
            {
                typeof(IAutoTaggingSpecification).Assembly,
            };

            container.RegisterMany(
                assemblies,
                serviceTypeCondition: type => type.IsInterface
                    && !string.IsNullOrWhiteSpace(type.FullName)
                    && !type.FullName.StartsWith("System"),
                reuse: Reuse.Singleton);

            container.RegisterMany(
                assemblies,
                serviceTypeCondition: type => !type.IsInterface
                    && !string.IsNullOrWhiteSpace(type.FullName)
                    && !type.FullName.StartsWith("System"),
                reuse: Reuse.Transient);

            return container;
        }

        [Test]
        public void Container_resolves_IEnumerable_IAutoTaggingSpecification_to_exactly_eleven_impls()
        {
            using var container = BuildRealContainer();

            var specs = container.Resolve<System.Collections.Generic.IEnumerable<IAutoTaggingSpecification>>().ToList();

            specs.Should().HaveCount(11,
                "DryIoc RegisterMany convention must auto-discover all 11 IAutoTaggingSpecification impls per 24-PLAN-DECISIONS.md Spec Catalog");
        }

        [Test]
        public void Container_registers_all_eight_sonarr_port_specs()
        {
            using var container = BuildRealContainer();
            var specs = container.Resolve<System.Collections.Generic.IEnumerable<IAutoTaggingSpecification>>().ToList();
            var typeNames = specs.Select(s => s.GetType().Name).ToList();

            typeNames.Should().Contain("GenreSpecification");
            typeNames.Should().Contain("YearSpecification");
            typeNames.Should().Contain("MonitoredSpecification");
            typeNames.Should().Contain("StatusSpecification");
            typeNames.Should().Contain("RootFolderSpecification");
            typeNames.Should().Contain("TranslationProfileSpecification");
            typeNames.Should().Contain("CustomFormatProfileSpecification");
            typeNames.Should().Contain("TagSpecification");
        }

        [Test]
        public void Container_registers_all_three_manga_new_specs()
        {
            using var container = BuildRealContainer();
            var specs = container.Resolve<System.Collections.Generic.IEnumerable<IAutoTaggingSpecification>>().ToList();
            var typeNames = specs.Select(s => s.GetType().Name).ToList();

            typeNames.Should().Contain("AuthorArtistSpecification");
            typeNames.Should().Contain("DemographicSpecification");
            typeNames.Should().Contain("ContentRatingSpecification");
        }

        [Test]
        public void Container_does_not_register_dropped_or_nonexistent_specs()
        {
            using var container = BuildRealContainer();
            var specs = container.Resolve<System.Collections.Generic.IEnumerable<IAutoTaggingSpecification>>().ToList();
            var typeNames = specs.Select(s => s.GetType().Name).ToList();

            typeNames.Should().NotContain("NetworkSpecification");
            typeNames.Should().NotContain("SeriesTypeSpecification");
            typeNames.Should().NotContain("OriginalCountrySpecification");
            typeNames.Should().NotContain("OriginalLanguageSpecification");
        }
    }
}
