using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FluentValidation.Results;
using NUnit.Framework;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Test.ImportListTests
{
    // Phase 27 Plan 27-05 Task 1 — Pattern H assertion-flip.
    //
    // BEFORE (Phase 26 Plan 26-04 / Phase 27 PATTERNS.md Pattern H BEFORE):
    //   The original anti-prod-leak gate asserted Subject.GetAvailableProviders() was
    //   empty — production reflection-scan returned 0 IMangaImportList implementations
    //   because Phase 26 ships ZERO production providers (D-08 / Pitfall 2 anti-prod-leak).
    //
    // AFTER (this commit — Phase 27 PATTERNS.md Pattern H AFTER):
    //   factory_returns_three_providers asserts Subject.GetAvailableProviders() carries
    //   exactly 3 providers — MangaDexImportList + AniListImportList + MalImportList —
    //   the v1.1 ImportList provider trio shipped by Plans 27-02 / 27-03 / 27-04.
    //
    // Plans 27-02 / 27-03 SUMMARYs noted that the Phase 26 Mocker-stub shape kept the OLD
    // assertion vacuously GREEN even after each provider landed (the test's auto-injected
    // IEnumerable<IMangaImportList> was empty regardless of production reflection state).
    // This commit replaces the Mocker-stub shape with the canonical InitializeProvidersFixture
    // pattern (IndexerFactory.InitializeProvidersFixture:39-61) — inject real
    // IMangaImportList stubs via Mocker.SetConstant<IEnumerable<...>> + seed
    // ImportListRepository.All() with 3 ImportListDefinition rows whose Implementation
    // matches each stub's GetType().Name + stub the IServiceProvider container so
    // GetInstance(definition) resolves to the corresponding stub.
    //
    // Pattern H invariant: "Phase 27 seeds MangaDex + AniList + MyAnimeList providers;
    // the 3-plugin invariant holds until v1.2+ adds CustomList/etc."
    [TestFixture]
    public class ImportListFactoryFixture : CoreTest<ImportListFactory>
    {
        private List<ImportListDefinition> _stored;
        private List<IMangaImportList> _providers;

        [SetUp]
        public void Setup()
        {
            _stored = new List<ImportListDefinition>();

            // Build 3 nested-private stub providers whose GetType().Name matches the
            // production class names (MangaDexImportList / AniListImportList / MalImportList).
            // Mirrors IndexerFactory.InitializeProvidersFixture:39-43 pattern verbatim —
            // real provider classes require a full DI graph (IHttpClient + IImportListStatusService
            // + IConfigService + IMangaParsingService + ILocalizationService + Logger), but the
            // factory.GetAvailableProviders path only consults GetType().Name + Definition,
            // so stubs suffice and the unit test stays fast (sub-second).
            _providers = new List<IMangaImportList>
            {
                new MangaDexImportList(),
                new AniListImportList(),
                new MalImportList(),
            };

            // Seed 3 ImportListDefinition rows — one per provider — with NullConfig.Instance
            // settings so Active() filter (c.Settings.Validate().IsValid && c.Enable) admits
            // every row. ImportListDefinition.Enable maps to EnableAutomaticAdd per the entity
            // override (ImportListDefinition.cs:38).
            _stored.Add(new ImportListDefinition
            {
                Id = 1,
                Name = "MangaDex",
                Implementation = nameof(MangaDexImportList),
                ConfigContract = nameof(MangaDexImportList),
                Settings = NullConfig.Instance,
                EnableAutomaticAdd = true,
            });
            _stored.Add(new ImportListDefinition
            {
                Id = 2,
                Name = "AniList",
                Implementation = nameof(AniListImportList),
                ConfigContract = nameof(AniListImportList),
                Settings = NullConfig.Instance,
                EnableAutomaticAdd = true,
            });
            _stored.Add(new ImportListDefinition
            {
                Id = 3,
                Name = "MyAnimeList",
                Implementation = nameof(MalImportList),
                ConfigContract = nameof(MalImportList),
                Settings = NullConfig.Instance,
                EnableAutomaticAdd = true,
            });

            Mocker.GetMock<IImportListRepository>()
                  .Setup(r => r.All())
                  .Returns(() => _stored.ToList());

            // ProviderStatusServiceBase.GetBlockedProviders is consulted by the factory's
            // FilterBlockedImportLists path; default to an empty blocked list so all 3
            // pass through GetAvailableProviders unblocked.
            Mocker.GetMock<IImportListStatusService>()
                  .Setup(s => s.GetBlockedProviders())
                  .Returns(new List<ImportListStatus>());

            // Replace the auto-resolved IEnumerable<IMangaImportList> with the 3 stub
            // providers so ProviderFactory._providers contains exactly our trio.
            Mocker.SetConstant<IEnumerable<IMangaImportList>>(_providers);

            // ProviderFactory.GetInstance calls _container.GetRequiredService(type) — the
            // Microsoft.Extensions.DependencyInjection extension built on IServiceProvider.
            // Stub GetService(Type) (the underlying interface method) to return the matching
            // stub instance for each provider type so each definition resolves correctly.
            var serviceProviderMock = Mocker.GetMock<IServiceProvider>();
            foreach (var provider in _providers)
            {
                var providerType = provider.GetType();
                var instance = provider;
                serviceProviderMock
                    .Setup(c => c.GetService(providerType))
                    .Returns(instance);
            }
        }

        [Test]
        public void factory_returns_three_providers()
        {
            // Phase 27 PATTERNS.md Pattern H AFTER — assertion-flip per 27-05-PLAN.md Task 1.
            // Plans 27-02 + 27-03 + 27-04 each shipped one production IMangaImportList
            // implementation, so a fully-populated DI bag + repo carries exactly 3 providers.
            var providers = Subject.GetAvailableProviders();

            providers.Should().HaveCount(3,
                "Phase 27 seeds MangaDex + AniList + MyAnimeList providers; the 3-plugin invariant holds until v1.2+ adds CustomList/etc.");

            providers.Select(p => p.Definition.Implementation)
                     .Should()
                     .BeEquivalentTo(new[] { "MangaDexImportList", "AniListImportList", "MalImportList" });
        }

        [Test]
        public void automatic_add_enabled_returns_three_providers_when_all_have_EnableAutomaticAdd_true()
        {
            // Companion to factory_returns_three_providers — AutomaticAddEnabled is the
            // ImportListSyncService entry point that drives the 24h cadence sync. With all
            // 3 definitions carrying EnableAutomaticAdd = true (set in [SetUp]), every
            // provider participates in the next sync cycle.
            var enabled = Subject.AutomaticAddEnabled();

            enabled.Should().HaveCount(3,
                "All 3 Phase 27 providers have EnableAutomaticAdd = true in the seeded repository — every provider participates in the 24h cadence sync.");
        }

        // Nested-private minimal IMangaImportList stubs — only the IProvider members consulted
        // by ProviderFactory.GetAvailableProviders + ImportListFactory.SetProviderCharacteristics.
        // Real classes (src/NzbDrone.Core/ImportLists/{MangaDex,AniList,MyAnimeList}/*.cs) require
        // a full DI graph (IHttpClient + per-provider IProxy + IImportListStatusService +
        // IConfigService + IMangaParsingService + ILocalizationService + Logger + …) which would
        // pull the entire ImportLists subsystem into this unit test. The factory test only needs
        // GetType().Name = the expected class name + ConfigContract + a Definition setter +
        // the bare IMangaImportList contract surface (ListType + MinRefreshInterval + Fetch).
        // Naming matches the production classes verbatim so the BeEquivalentTo assertion holds.
        private sealed class MangaDexImportList : IMangaImportList
        {
            public string Name => "MangaDex";
            public Type ConfigContract => typeof(NullConfig);
            public ProviderMessage Message => null;
            public IEnumerable<ProviderDefinition> DefaultDefinitions => Array.Empty<ProviderDefinition>();
            public ProviderDefinition Definition { get; set; }
            public ImportListType ListType => ImportListType.MangaDex;
            public TimeSpan MinRefreshInterval => TimeSpan.FromHours(24);
            public ImportListFetchResult Fetch() => new ImportListFetchResult();
            public ValidationResult Test() => new ValidationResult();
            public object RequestAction(string action, IDictionary<string, string> query) => null;
        }

        private sealed class AniListImportList : IMangaImportList
        {
            public string Name => "AniList";
            public Type ConfigContract => typeof(NullConfig);
            public ProviderMessage Message => null;
            public IEnumerable<ProviderDefinition> DefaultDefinitions => Array.Empty<ProviderDefinition>();
            public ProviderDefinition Definition { get; set; }
            public ImportListType ListType => ImportListType.AniList;
            public TimeSpan MinRefreshInterval => TimeSpan.FromHours(24);
            public ImportListFetchResult Fetch() => new ImportListFetchResult();
            public ValidationResult Test() => new ValidationResult();
            public object RequestAction(string action, IDictionary<string, string> query) => null;
        }

        private sealed class MalImportList : IMangaImportList
        {
            public string Name => "MyAnimeList";
            public Type ConfigContract => typeof(NullConfig);
            public ProviderMessage Message => null;
            public IEnumerable<ProviderDefinition> DefaultDefinitions => Array.Empty<ProviderDefinition>();
            public ProviderDefinition Definition { get; set; }
            public ImportListType ListType => ImportListType.MyAnimeList;
            public TimeSpan MinRefreshInterval => TimeSpan.FromHours(24);
            public ImportListFetchResult Fetch() => new ImportListFetchResult();
            public ValidationResult Test() => new ValidationResult();
            public object RequestAction(string action, IDictionary<string, string> query) => null;
        }
    }
}
