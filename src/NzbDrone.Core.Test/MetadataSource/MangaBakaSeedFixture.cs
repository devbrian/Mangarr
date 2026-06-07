using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.MetadataSource.MangaBaka;
using NzbDrone.Core.MetadataSource.MangaDex;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource
{
    // Phase 41 (Plan 41-05) — the binding proof of RESEARCH Open Question 2's correctness
    // criterion: "exactly one MangaBaka primary row exists post-upgrade on a non-empty
    // MetadataSources table, AND the single-primary invariant is never violated" — on BOTH
    // a fresh DB and an upgraded (non-empty) DB.
    //
    // The seam under test (MetadataSourceFactory.InitializeProviders) was authored in Plan
    // 41-01 but could only be proven end-to-end once the real MangaBaka provider existed
    // (Plan 41-03 — DefaultIsPrimary MangaBaka=true / MangaDex=false). This fixture closes
    // that proof.
    //
    // The seed is triggered through the REAL ProviderFactory.Handle(ApplicationStartedEvent)
    // seam (RemoveMissingImplementations -> InitializeProviders), not by calling the
    // protected InitializeProviders directly — so the purge-then-seed flow is modelled
    // faithfully (mirrors InitializeProvidersFixture for the indexer factory).
    [TestFixture]
    public class MangaBakaSeedFixture : CoreTest<MetadataSourceFactory>
    {
        private List<MetadataSourceDefinition> _stored;
        private List<IMetadataSource> _providers;

        [SetUp]
        public void Setup()
        {
            _stored = new List<MetadataSourceDefinition>();

            // Real provider instances per the plan: DefaultDefinitions reads only
            // GetType().Name + DefaultIsPrimary, so no HTTP/transport call is made when the
            // factory enumerates the seed targets. AniList is included so the explicit-primary
            // test's stored AniList row survives RemoveMissingImplementations (which purges any
            // stored Implementation that does not resolve to a registered IMetadataSource).
            _providers = new List<IMetadataSource>
            {
                new MangaBakaMetadataSource(Mocker.GetMock<IHttpClient>().Object, TestLogger),
                new MangaDexMetadataSource(Mocker.GetMock<IHttpClient>().Object, TestLogger),
                new AniListMetadataSource(
                    Mocker.GetMock<IHttpClient>().Object,
                    Mocker.GetMock<IAniListGraphQlTransport>().Object,
                    TestLogger),
            };

            Mocker.GetMock<IMetadataSourceRepository>()
                  .Setup(r => r.All())
                  .Returns(() => _stored.ToList());

            Mocker.GetMock<IMetadataSourceRepository>()
                  .Setup(r => r.Insert(It.IsAny<MetadataSourceDefinition>()))
                  .Callback<MetadataSourceDefinition>(d =>
                  {
                      d.Id = _stored.Count + 1;
                      _stored.Add(d);
                  })
                  .Returns<MetadataSourceDefinition>(d => d);

            // Model RemoveMissingImplementations faithfully: the base ProviderFactory.Handle
            // Deletes every stored def whose Implementation does not resolve to a registered
            // provider BEFORE InitializeProviders runs. Mutating _stored here keeps the purge
            // observable rather than a silent no-op.
            Mocker.GetMock<IMetadataSourceRepository>()
                  .Setup(r => r.Delete(It.IsAny<MetadataSourceDefinition>()))
                  .Callback<MetadataSourceDefinition>(d => _stored.RemoveAll(x => x.Id == d.Id));

            Mocker.SetConstant<IEnumerable<IMetadataSource>>(_providers);
        }

        // (1) Fresh DB: the seed lands exactly one primary and it is MangaBaka. MangaDex's
        // DefaultIsPrimary is now false (Plan 41-03), so it is NOT auto-seeded as primary —
        // and because its DefaultDefinitions exposes no primary default it is not auto-seeded
        // at all on the fresh path. The single-primary invariant holds (Count(IsPrimary) == 1).
        [Test]
        public void Fresh_DB_seeds_exactly_one_primary_and_it_is_MangaBaka()
        {
            Subject.Handle(new ApplicationStartedEvent());

            _stored.Count(d => d.IsPrimary).Should().Be(1, "the fresh-DB seed must yield exactly one primary");
            _stored.Single(d => d.IsPrimary).Implementation.Should().Be(nameof(MangaBakaMetadataSource));
        }

        // (2) Upgraded DB with NO primary (the post-Migration-012 demoted state — MangaDex
        // present but non-primary, MangaBaka absent): the backfill creates a MangaBaka row and
        // promotes it because no primary exists. MangaDex is KEPT and stays non-primary (D-01).
        // The single-primary invariant holds.
        [Test]
        public void Upgraded_DB_with_no_primary_backfills_one_MangaBaka_primary()
        {
            _stored.Add(new MetadataSourceDefinition
            {
                Id = 1,
                Name = "MangaDex",
                Implementation = nameof(MangaDexMetadataSource),
                ConfigContract = nameof(MangaDexMetadataSourceSettings),
                IsPrimary = false,
            });

            Subject.Handle(new ApplicationStartedEvent());

            _stored.Count(d => d.IsPrimary).Should().Be(1, "the upgrade backfill must yield exactly one primary");
            _stored.Single(d => d.IsPrimary).Implementation.Should().Be(nameof(MangaBakaMetadataSource));

            // KEEP MangaDex (D-01) — still present, still non-primary.
            var mangaDex = _stored.Single(d => d.Implementation == nameof(MangaDexMetadataSource));
            mangaDex.IsPrimary.Should().BeFalse();
        }

        // (3) Upgraded DB with an explicit non-MangaDex primary (AniList primary + MangaDex
        // non-primary, MangaBaka absent): the user's explicit choice is preserved (D-01 guard).
        // AniList stays primary, and MangaBaka backfills NON-primary because a primary already
        // exists. The single-primary invariant holds.
        [Test]
        public void Upgraded_DB_with_explicit_non_mangadex_primary_is_left_untouched()
        {
            _stored.Add(new MetadataSourceDefinition
            {
                Id = 1,
                Name = "MangaDex",
                Implementation = nameof(MangaDexMetadataSource),
                ConfigContract = nameof(MangaDexMetadataSourceSettings),
                IsPrimary = false,
            });
            _stored.Add(new MetadataSourceDefinition
            {
                Id = 2,
                Name = "AniList",
                Implementation = nameof(AniListMetadataSource),
                ConfigContract = nameof(AniListMetadataSourceSettings),
                IsPrimary = true,
            });

            Subject.Handle(new ApplicationStartedEvent());

            _stored.Count(d => d.IsPrimary).Should().Be(1, "an explicit primary must keep the single-primary invariant");

            // The explicit AniList primary survives untouched (D-01 guard preserves user choice).
            _stored.Single(d => d.IsPrimary).Implementation.Should().Be(nameof(AniListMetadataSource));

            // MangaBaka is backfilled but NON-primary (a primary already existed).
            var mangaBaka = _stored.Single(d => d.Implementation == nameof(MangaBakaMetadataSource));
            mangaBaka.IsPrimary.Should().BeFalse();
        }
    }
}
