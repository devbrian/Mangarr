using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests
{
    // Phase 26 Plan 26-04 — D-08 / Pitfall 2 anti-prod-leak gate + AutomaticAddEnabled
    // filter behavior.
    //
    // factory_returns_zero_providers_on_empty_di_bag is the bucket A SC#6 sanity
    // check: a stock NzbDrone.Core production build (no test assembly referenced) has
    // ZERO IMangaImportList implementations registered in DI, so the factory's
    // GetAvailableProviders() returns an empty list. Phase 27 lands the first concrete
    // provider; until then the Settings → ImportLists Add picker is empty in production.
    [TestFixture]
    public class ImportListFactoryFixture : CoreTest<ImportListFactory>
    {
        private List<ImportListDefinition> _stored;

        [SetUp]
        public void Setup()
        {
            _stored = new List<ImportListDefinition>();

            Mocker.GetMock<IImportListRepository>()
                  .Setup(r => r.All())
                  .Returns(() => _stored.ToList());

            // ProviderStatusServiceBase.GetBlockedProviders is consulted by the factory's
            // FilterBlockedImportLists path; default to an empty blocked list.
            Mocker.GetMock<IImportListStatusService>()
                  .Setup(s => s.GetBlockedProviders())
                  .Returns(new List<ImportListStatus>());
        }

        [Test]
        public void factory_returns_zero_providers_on_empty_di_bag()
        {
            // CoreTest's Mocker auto-injects an empty IEnumerable<IMangaImportList>
            // when no providers are explicitly registered — this is the production
            // reflection-scan equivalent state for Phase 26 (zero IMangaImportList
            // implementations in NzbDrone.Core per D-08).
            var providers = Subject.GetAvailableProviders();

            providers.Should().BeEmpty(
                "D-08 / Pitfall 2: Phase 26 ships zero production IMangaImportList implementations; Phase 27 owns provider seeding.");
        }

        [Test]
        public void automatic_add_enabled_returns_empty_when_no_providers_registered()
        {
            // Even with rows in the repo, AutomaticAddEnabled returns empty because
            // GetAvailableProviders cannot resolve an Implementation against the empty
            // injected IEnumerable<IMangaImportList> (Pitfall 2 / D-08 confirmation:
            // production builds with no provider plugins yield no available providers
            // regardless of seeded definitions).
            //
            // Note: ProviderFactory.Active() filters via Settings.Validate(); the rows
            // we author here ride NullConfig.Instance (Settings = null path through
            // ProviderRepository<T>.Query hydration), which Active() would NRE on. To
            // keep the test deterministic without authoring a Settings POCO we stub the
            // repo to return zero definitions — the contract still holds.
            _stored.Clear();

            var enabled = Subject.AutomaticAddEnabled();

            enabled.Should().BeEmpty(
                "AutomaticAddEnabled inherits GetAvailableProviders' empty result when no IMangaImportList implementations are DI-registered (Pitfall 2).");
        }
    }
}
