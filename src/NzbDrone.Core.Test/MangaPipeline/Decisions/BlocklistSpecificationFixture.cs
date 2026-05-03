using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Blocklisting.Manga;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Manga.Specifications;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Test.MangaPipeline.Decisions
{
    // Phase 6 Wave 1 BLOCKING fixture — D-19 STUB-replacement target.
    // Wired by Plan 06-04 (MangaBlocklistService) + the BlocklistSpecification STUB body
    // replacement that calls _mangaBlocklistService.Blocklisted.
    //
    // Pitfall 6 GUARD: this fixture instantiates BlocklistSpecification with a mock
    // IMangaBlocklistService directly. The Wave 5 F-01 BLOCKING fixture (Plan 06-12) is the
    // real-DI consumer that proves the auto-discovery 11-spec count remains stable.
    [TestFixture]
    public class BlocklistSpecificationFixture : MangaPipelineTestBase
    {
        private BlocklistSpecification _spec;
        private Mock<IMangaBlocklistService> _blocklistService;

        [SetUp]
        public void Setup()
        {
            _blocklistService = new Mock<IMangaBlocklistService>();
            _spec = new BlocklistSpecification(_blocklistService.Object, LogManager.GetLogger("test"));
        }

        private RemoteChapter BuildRemoteChapter(int mangaId = 7, string releaseTitle = "Test Manga - 0001")
        {
            return new RemoteChapter
            {
                Manga = new NzbDrone.Core.Manga.Manga { Id = mangaId, Title = "Test Manga" },
                Chapters = new List<Chapter>
                {
                    new() { Id = 42, MangaId = mangaId, Monitored = true, ChapterNumber = 1m }
                },
                Release = new ReleaseInfo
                {
                    Title = releaseTitle,
                    Indexer = "MangaDex",
                    Guid = "g1"
                }
            };
        }

        [Test]
        public void Rejects_when_blocklisted()
        {
            _blocklistService.Setup(s => s.Blocklisted(7, It.IsAny<ReleaseInfo>())).Returns(true);

            var subject = BuildRemoteChapter();
            var decision = _spec.IsSatisfiedBy(subject, new ReleaseDecisionInformation());

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(DownloadRejectionReason.Blocklisted);
            _blocklistService.Verify(s => s.Blocklisted(7, subject.Release), Times.Once);
        }

        [Test]
        public void Accepts_when_not_blocklisted()
        {
            _blocklistService.Setup(s => s.Blocklisted(7, It.IsAny<ReleaseInfo>())).Returns(false);

            var subject = BuildRemoteChapter();
            var decision = _spec.IsSatisfiedBy(subject, new ReleaseDecisionInformation());

            decision.Accepted.Should().BeTrue();
            _blocklistService.Verify(s => s.Blocklisted(7, subject.Release), Times.Once);
        }
    }
}
