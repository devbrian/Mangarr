using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Manga.Specifications;
using NzbDrone.Core.IndexerSearch.Definitions;

namespace NzbDrone.Core.Test.DecisionEngineTests.Manga
{
    // GH #118 — fixture for the existing MangaSpecification (manga peer of
    // Sonarr's deleted SeriesSpecification). Pre-GH #118 this spec was
    // structurally tautological on the search path because
    // MangaDownloadDecisionMaker force-assigned `subject.Manga = searchCriteria.Manga`
    // (commit 77a114221). With the force-assign reverted (parts 3+5 of the
    // GH #118 fix), MangaParsingService.GetManga is the resolver again and
    // this spec catches the case where the parsed release title resolves to
    // a DIFFERENT manga than the search target — i.e. cross-title indexer
    // noise.
    //
    // RSS path coverage (searchCriteria == null → Accept) is already exercised
    // by the production code path through the unchanged top branch of
    // MangaSpecification.IsSatisfiedBy.
    [TestFixture]
    public class MangaSpecificationFixture
        : MangaDecisionEngineSpecFixtureBase<MangaSpecification>
    {
        [Test]
        public void Accepts_on_RSS_path_when_no_search_criteria()
        {
            var rc = BuildRemoteChapter();
            var info = new ReleaseDecisionInformation { MangaSearchCriteria = null };

            var result = Subject.IsSatisfiedBy(rc, info);

            result.Accepted.Should().BeTrue();
        }

        [Test]
        public void Accepts_on_search_path_when_resolved_manga_matches_search_target()
        {
            // Happy path post-GH-118: GetManga resolved (via Strategy 1 / 2 / 3) to
            // the same manga the user searched for. MangaSpecification accepts;
            // downstream specs continue evaluation.
            var rc = BuildRemoteChapter();
            var info = new ReleaseDecisionInformation
            {
                MangaSearchCriteria = new MangaSearchCriteria
                {
                    Manga = rc.Manga,
                    Chapters = new List<NzbDrone.Core.Manga.Chapter>(),
                },
            };

            var result = Subject.IsSatisfiedBy(rc, info);

            result.Accepted.Should().BeTrue();
        }

        [Test]
        public void Rejects_on_search_path_when_resolved_manga_differs_from_search_target()
        {
            // GH #118 core scenario: indexer returns a release whose parsed
            // MangaTitle resolves (via GetManga) to manga B, but the user
            // searched for manga A. Pre-fix this was force-attributed to manga A
            // and grabbable; post-fix MangaSpecification rejects as wrong manga.
            var rc = BuildRemoteChapter(); // Manga.Id = 1 per base helper

            var differentSearchTarget = new NzbDrone.Core.Manga.Manga
            {
                Id = 999,
                Title = "Different Manga",
                CleanTitle = "different manga",
            };

            var info = new ReleaseDecisionInformation
            {
                MangaSearchCriteria = new MangaSearchCriteria
                {
                    Manga = differentSearchTarget,
                    Chapters = new List<NzbDrone.Core.Manga.Chapter>(),
                },
            };

            var result = Subject.IsSatisfiedBy(rc, info);

            result.Accepted.Should().BeFalse();
            result.Reason.Should().Be(DownloadRejectionReason.MatchesAnotherSeries);
        }
    }
}
