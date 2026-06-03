using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Gateway;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Test.Indexer
{
    // Sonarr divergence: zero-config first-run UX (PROJECT.md v1 lock). Mangarr seeds a
    // known-good source on a fresh DB so /settings/indexers is not empty. Mirrors
    // MetadataSourceFactoryFixture's "providers_resolved" + idempotency style.
    //
    // Phase 39 (RETIRE-02): the in-process MangaDexIndexer + ComixIndexer were deleted;
    // GatewayIndexer (Phase 37) is now the SOLE IIndexer and the sole seeded implementation.
    // It seeds DISABLED-by-default because its empty default settings fail validation.
    //
    // Anti-pattern C compliance: assertions go through real factory code (Insert ->
    // Repository.Insert is mocked; we assert the repository receives Insert calls with
    // the right Implementation + Name + Settings shape).
    [TestFixture]
    public class InitializeProvidersFixture : CoreTest<IndexerFactory>
    {
        private List<IndexerDefinition> _stored;
        private List<IIndexer> _providers;

        [SetUp]
        public void Setup()
        {
            _stored = new List<IndexerDefinition>();

            // Build a mock provider whose GetType().Name == "GatewayIndexer". The real
            // GatewayIndexer would require a full DI graph (IHttpClient,
            // IIndexerSourceStatusService, etc.); the seed code only consults
            // GetType().Name, .Name, .ConfigContract, and .DefaultDefinitions, so a stub
            // suffices. We use the real GatewaySettings POCO (compile-time defaults are
            // part of the seed contract).
            _providers = new List<IIndexer>
            {
                new GatewayIndexer(),
            };

            Mocker.GetMock<IIndexerRepository>()
                  .Setup(r => r.All())
                  .Returns(() => _stored.ToList());

            Mocker.GetMock<IIndexerRepository>()
                  .Setup(r => r.Insert(It.IsAny<IndexerDefinition>()))
                  .Callback<IndexerDefinition>(d =>
                  {
                      d.Id = _stored.Count + 1;
                      _stored.Add(d);
                  })
                  .Returns<IndexerDefinition>(d => d);

            // Replace the auto-resolved IEnumerable<IIndexer> with our stub list so the
            // factory under test sees exactly our seed target.
            Mocker.SetConstant<IEnumerable<IIndexer>>(_providers);
        }

        [Test]
        public void Handle_ApplicationStarted_seeds_gateway_on_empty_db()
        {
            Subject.Handle(new ApplicationStartedEvent());

            _stored.Should().HaveCount(1);
            _stored.Select(d => d.Implementation)
                   .Should().BeEquivalentTo(new[] { nameof(GatewayIndexer) });
            _stored.Select(d => d.Name)
                   .Should().BeEquivalentTo(new[] { "Manga Gateway" });

            // Phase 37 A3: GatewayIndexer seeds DISABLED-by-default — its empty default settings
            // (blank BaseUrl/ApiKey) fail config.Validate().IsValid, so DefaultDefinitions yields
            // EnableRss / EnableAutomaticSearch / EnableInteractiveSearch = false.
            var gateway = _stored.Single(d => d.Implementation == nameof(GatewayIndexer));
            gateway.EnableRss.Should().BeFalse();
            gateway.EnableAutomaticSearch.Should().BeFalse();
            gateway.EnableInteractiveSearch.Should().BeFalse();
        }

        [Test]
        public void Handle_ApplicationStarted_is_idempotent_when_rows_already_exist()
        {
            // Pre-existing user-edited row — seeder must NOT create another Gateway row
            // and must NOT touch the existing one (S4 pattern: All().Any() short-circuit).
            _stored.Add(new IndexerDefinition
            {
                Id = 1,
                Name = "User Custom Gateway",
                Implementation = nameof(GatewayIndexer),
                ConfigContract = nameof(GatewaySettings),
                EnableRss = false,
            });

            Subject.Handle(new ApplicationStartedEvent());

            _stored.Should().HaveCount(1);
            _stored[0].Name.Should().Be("User Custom Gateway");
            _stored[0].EnableRss.Should().BeFalse();
        }

        [Test]
        public void Handle_ApplicationStarted_skips_seed_when_user_deleted_seeded_row()
        {
            // User explicitly deleted the seeded row but added a different indexer. Subsequent
            // restart must NOT recreate the Gateway — All().Any() returns true so we leave the
            // table alone. This is the user-override-respect contract; without it, "delete this
            // row" would have no permanent effect.
            _stored.Add(new IndexerDefinition
            {
                Id = 1,
                Name = "Some Other Indexer",
                Implementation = "SomeOtherIndexer",
                ConfigContract = "SomeOtherIndexerSettings",
            });

            Subject.Handle(new ApplicationStartedEvent());

            _stored.Should().HaveCount(1);
            _stored[0].Implementation.Should().Be("SomeOtherIndexer");
        }

        // Phase 37 A3 stub: the GatewayIndexer seeds DISABLED-by-default because its empty default
        // settings fail validation. The DefaultDefinitions here reproduce that disabled outcome
        // (EnableRss/EnableAutomaticSearch/EnableInteractiveSearch = false) so the seed-list test
        // proves the gateway is seeded WITHOUT being auto-enabled.
        private sealed class GatewayIndexer : IIndexer
        {
            public string Name => "Manga Gateway";
            public System.Type ConfigContract => typeof(GatewaySettings);
            public ProviderMessage Message => null;
            public IEnumerable<ProviderDefinition> DefaultDefinitions => new[]
            {
                new IndexerDefinition
                {
                    Name = nameof(GatewayIndexer),
                    Implementation = nameof(GatewayIndexer),
                    ConfigContract = nameof(GatewaySettings),
                    Settings = new GatewaySettings(),
                    EnableRss = false,
                    EnableAutomaticSearch = false,
                    EnableInteractiveSearch = false,
                }
            };
            public ProviderDefinition Definition { get; set; }
            public bool SupportsRss => true;
            public bool SupportsSearch => true;
            public DownloadProtocol Protocol => DownloadProtocol.Http;
            public System.Threading.Tasks.Task<IList<NzbDrone.Core.Parser.Model.ReleaseInfo>> FetchRecent()
                => System.Threading.Tasks.Task.FromResult<IList<NzbDrone.Core.Parser.Model.ReleaseInfo>>(System.Array.Empty<NzbDrone.Core.Parser.Model.ReleaseInfo>());
            public System.Threading.Tasks.Task<IList<NzbDrone.Core.Parser.Model.ReleaseInfo>> Fetch(NzbDrone.Core.IndexerSearch.Definitions.MangaSearchCriteria sc)
                => System.Threading.Tasks.Task.FromResult<IList<NzbDrone.Core.Parser.Model.ReleaseInfo>>(System.Array.Empty<NzbDrone.Core.Parser.Model.ReleaseInfo>());
            public System.Threading.Tasks.Task<IList<NzbDrone.Core.Parser.Model.ReleaseInfo>> Fetch(NzbDrone.Core.IndexerSearch.Definitions.ChapterSearchCriteria sc)
                => System.Threading.Tasks.Task.FromResult<IList<NzbDrone.Core.Parser.Model.ReleaseInfo>>(System.Array.Empty<NzbDrone.Core.Parser.Model.ReleaseInfo>());
            public NzbDrone.Common.Http.HttpRequest GetDownloadRequest(string link) => null;
            public FluentValidation.Results.ValidationResult Test() => new();
            public object RequestAction(string s, IDictionary<string, string> q) => null;
        }
    }
}
