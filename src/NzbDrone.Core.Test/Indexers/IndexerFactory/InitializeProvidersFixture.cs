using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Comix;
using NzbDrone.Core.Indexers.MangaDex;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Test.Indexer
{
    // Sonarr divergence: zero-config first-run UX (PROJECT.md v1 lock). Mangarr seeds
    // MangaDex (BEDROCK) + Comix (reference port #1) on a fresh DB. Mirrors
    // MetadataSourceFactoryFixture's "All_three_providers_resolved" + idempotency style.
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

            // Build mock providers whose GetType().Name == "MangaDexIndexer" / "ComixIndexer".
            // Real ComixIndexer / MangaDexIndexer would require a full DI graph (IHttpClient,
            // IIndexerSourceStatusService, etc.); the seed code only consults
            // GetType().Name, .Name, .ConfigContract, and .DefaultDefinitions, so a stub
            // suffices. We use the real ComixIndexerSettings / MangaDexIndexerSettings POCOs
            // (compile-time defaults are part of the seed contract).
            _providers = new List<IIndexer>
            {
                new MangaDexIndexer(),
                new ComixIndexer(),
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
            // factory under test sees exactly our two seed targets.
            Mocker.SetConstant<IEnumerable<IIndexer>>(_providers);
        }

        [Test]
        public void Handle_ApplicationStarted_seeds_mangadex_and_comix_on_empty_db()
        {
            Subject.Handle(new ApplicationStartedEvent());

            _stored.Should().HaveCount(2);
            _stored.Select(d => d.Implementation)
                   .Should().BeEquivalentTo(new[] { nameof(MangaDexIndexer), nameof(ComixIndexer) });
            _stored.Select(d => d.Name)
                   .Should().BeEquivalentTo(new[] { "MangaDex", "Comix" });

            // The seeded rows pull defaults straight from each provider's
            // DefaultDefinitions — EnableRss / EnableAutomaticSearch / EnableInteractiveSearch
            // are all true (validate() passes; SupportsRss + SupportsSearch both true).
            _stored.Single(d => d.Implementation == nameof(MangaDexIndexer))
                   .EnableRss.Should().BeTrue();
            _stored.Single(d => d.Implementation == nameof(ComixIndexer))
                   .EnableAutomaticSearch.Should().BeTrue();
        }

        [Test]
        public void Handle_ApplicationStarted_is_idempotent_when_rows_already_exist()
        {
            // Pre-existing user-edited row — seeder must NOT create another MangaDex row
            // and must NOT touch the existing one (S4 pattern: All().Any() short-circuit).
            _stored.Add(new IndexerDefinition
            {
                Id = 1,
                Name = "User Custom MangaDex",
                Implementation = nameof(MangaDexIndexer),
                ConfigContract = nameof(MangaDexIndexerSettings),
                EnableRss = false,
            });

            Subject.Handle(new ApplicationStartedEvent());

            _stored.Should().HaveCount(1);
            _stored[0].Name.Should().Be("User Custom MangaDex");
            _stored[0].EnableRss.Should().BeFalse();
        }

        [Test]
        public void Handle_ApplicationStarted_skips_seed_when_user_deleted_one_seeded_row()
        {
            // User explicitly deleted MangaDex but kept Comix. Subsequent restart must NOT
            // recreate MangaDex — All().Any() returns true so we leave the table alone.
            // This is the user-override-respect contract; without it, "delete this row"
            // would have no permanent effect.
            _stored.Add(new IndexerDefinition
            {
                Id = 1,
                Name = "Comix",
                Implementation = nameof(ComixIndexer),
                ConfigContract = nameof(ComixIndexerSettings),
            });

            Subject.Handle(new ApplicationStartedEvent());

            _stored.Should().HaveCount(1);
            _stored[0].Implementation.Should().Be(nameof(ComixIndexer));
        }

        // Minimal stubs: only the IProvider members consulted by InitializeProviders.
        private sealed class MangaDexIndexer : IIndexer
        {
            public string Name => "MangaDex";
            public System.Type ConfigContract => typeof(MangaDexIndexerSettings);
            public ProviderMessage Message => null;
            public IEnumerable<ProviderDefinition> DefaultDefinitions => new[]
            {
                new IndexerDefinition
                {
                    Name = nameof(MangaDexIndexer),
                    Implementation = nameof(MangaDexIndexer),
                    ConfigContract = nameof(MangaDexIndexerSettings),
                    Settings = new MangaDexIndexerSettings(),
                    EnableRss = true,
                    EnableAutomaticSearch = true,
                    EnableInteractiveSearch = true,
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

        private sealed class ComixIndexer : IIndexer
        {
            public string Name => "Comix";
            public System.Type ConfigContract => typeof(ComixIndexerSettings);
            public ProviderMessage Message => null;
            public IEnumerable<ProviderDefinition> DefaultDefinitions => new[]
            {
                new IndexerDefinition
                {
                    Name = nameof(ComixIndexer),
                    Implementation = nameof(ComixIndexer),
                    ConfigContract = nameof(ComixIndexerSettings),
                    Settings = new ComixIndexerSettings(),
                    EnableRss = true,
                    EnableAutomaticSearch = true,
                    EnableInteractiveSearch = true,
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
