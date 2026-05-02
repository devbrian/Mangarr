using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource
{
    // Plan 02-05 (Wave 2 IMetadataSource ThingiProvider scaffold) — META-05 + threat
    // T-CONFIG-DRIFT-01.
    //
    // The factory-only behaviors (SetPrimary atomic demote/promote, GetPrimary, id
    // validation) flip green now. The "All_three_providers_resolved" RED stays
    // [Ignore]'d until Wave 3 (Plans 02-06..02-08) lands the three concrete providers.
    //
    // Acceptance literal anchors required by Plan 02-01 Task 2 acceptance:
    //   * All_three_providers_resolved (test method below)
    //   * SetPrimary, GetPrimary (test methods below)
    [TestFixture]
    public class MetadataSourceFactoryFixture : CoreTest<MetadataSourceFactory>
    {
        private List<MetadataSourceDefinition> _stored;

        [SetUp]
        public void Setup()
        {
            _stored = new List<MetadataSourceDefinition>
            {
                new MetadataSourceDefinition
                {
                    Id = 1,
                    Name = "MangaDex",
                    Implementation = "MangaDexMetadataSource",
                    ConfigContract = "MangaDexMetadataSourceSettings",
                    IsPrimary = true,
                },
                new MetadataSourceDefinition
                {
                    Id = 2,
                    Name = "AniList",
                    Implementation = "AniListMetadataSource",
                    ConfigContract = "AniListMetadataSourceSettings",
                    IsPrimary = false,
                },
                new MetadataSourceDefinition
                {
                    Id = 3,
                    Name = "MyAnimeList",
                    Implementation = "MyAnimeListMetadataSource",
                    ConfigContract = "MyAnimeListMetadataSourceSettings",
                    IsPrimary = false,
                },
            };

            Mocker.GetMock<IMetadataSourceRepository>()
                  .Setup(r => r.All())
                  .Returns(() => _stored.ToList());

            Mocker.GetMock<IMetadataSourceRepository>()
                  .Setup(r => r.UpdateMany(It.IsAny<IList<MetadataSourceDefinition>>()))
                  .Callback<IList<MetadataSourceDefinition>>(list =>
                  {
                      foreach (var d in list)
                      {
                          var existing = _stored.FirstOrDefault(s => s.Id == d.Id);
                          if (existing != null)
                          {
                              existing.IsPrimary = d.IsPrimary;
                          }
                      }
                  });
        }

        // Per Pitfall 4 in 02-RESEARCH: assert injected IEnumerable<IMetadataSource> count == 3.
        [Test]
        [Ignore("RED until Wave 3 — Plans 02-06/02-07/02-08 land the three concrete IMetadataSource providers. Factory wiring + auto-discovery test belongs to those plans.")]
        public void All_three_providers_resolved() => Assert.Inconclusive("Wave 3");

        // SetPrimary(2) flips id=1 IsPrimary=true → false and id=2 IsPrimary=false → true
        // (atomic). Covers META-05 + threat T-CONFIG-DRIFT-01.
        [Test]
        public void SetPrimary_demotes_prior_primary_when_promoting_another()
        {
            Subject.SetPrimary(2);

            _stored.Single(d => d.Id == 1).IsPrimary.Should().BeFalse();
            _stored.Single(d => d.Id == 2).IsPrimary.Should().BeTrue();
            _stored.Single(d => d.Id == 3).IsPrimary.Should().BeFalse();

            // The invariant: at-most-one IsPrimary=true (D-15).
            _stored.Count(d => d.IsPrimary).Should().Be(1);
        }

        [Test]
        public void GetPrimary_returns_the_one_with_IsPrimary_true()
        {
            var primary = Subject.GetPrimary();

            primary.Should().NotBeNull();
            primary.Id.Should().Be(1);
            primary.Name.Should().Be("MangaDex");
            primary.IsPrimary.Should().BeTrue();
        }

        [Test]
        public void SetPrimary_throws_when_id_not_found()
        {
            Action act = () => Subject.SetPrimary(9999);

            act.Should().Throw<InvalidOperationException>()
               .WithMessage("*9999*not found*");
        }
    }
}
