using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MediaFiles.MangaImport;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.MangaImport
{
    // Phase 6 Plan 06-07 — MangaImportDecisionMaker tests:
    //   1. Auto-resolves IEnumerable<IMangaImportDecisionEngineSpecification> via DryIoc
    //      (verified by feeding mocked specs into the ctor and observing they all run).
    //   2. Aggregates rejection reasons across multiple specs.
    //   3. Spec-level exception is contained (returns DecisionError reason; doesn't poison batch).
    [TestFixture]
    public class MangaImportDecisionMakerFixture : CoreTest<MangaImportDecisionMaker>
    {
        private LocalChapter _localChapter;

        [SetUp]
        public void Setup()
        {
            var manga = Builder<NzbDrone.Core.Manga.Manga>.CreateNew().With(m => m.Id = 1).Build();
            var chapter = Builder<Chapter>.CreateNew().With(c => c.Id = 99).Build();
            _localChapter = new LocalChapter
            {
                Manga = manga,
                Chapter = chapter,
                Chapters = new List<Chapter> { chapter },
                Path = @"C:\Staging\test.cbz",
                Size = 100
            };
        }

        private MangaImportDecisionMaker Make(params IMangaImportDecisionEngineSpecification[] specs)
        {
            return new MangaImportDecisionMaker(specs, TestLogger);
        }

        [Test]
        public void should_run_every_spec_against_the_local_chapter()
        {
            var specA = new Mock<IMangaImportDecisionEngineSpecification>();
            specA.Setup(s => s.IsSatisfiedBy(It.IsAny<LocalChapter>(), It.IsAny<DownloadClientItem>()))
                 .Returns(MangaImportSpecDecision.Accept());
            var specB = new Mock<IMangaImportDecisionEngineSpecification>();
            specB.Setup(s => s.IsSatisfiedBy(It.IsAny<LocalChapter>(), It.IsAny<DownloadClientItem>()))
                 .Returns(MangaImportSpecDecision.Accept());

            var subject = Make(specA.Object, specB.Object);

            var decision = subject.GetDecision(_localChapter, null);

            decision.Approved.Should().BeTrue();
            specA.Verify(s => s.IsSatisfiedBy(_localChapter, null), Times.Once);
            specB.Verify(s => s.IsSatisfiedBy(_localChapter, null), Times.Once);
        }

        [Test]
        public void should_aggregate_rejections_from_multiple_specs()
        {
            var specA = new Mock<IMangaImportDecisionEngineSpecification>();
            specA.Setup(s => s.IsSatisfiedBy(It.IsAny<LocalChapter>(), It.IsAny<DownloadClientItem>()))
                 .Returns(MangaImportSpecDecision.Reject(ImportRejectionReason.EmptyArchive, "empty"));
            var specB = new Mock<IMangaImportDecisionEngineSpecification>();
            specB.Setup(s => s.IsSatisfiedBy(It.IsAny<LocalChapter>(), It.IsAny<DownloadClientItem>()))
                 .Returns(MangaImportSpecDecision.Reject(ImportRejectionReason.MinimumFreeSpace, "no space"));

            var subject = Make(specA.Object, specB.Object);

            var decision = subject.GetDecision(_localChapter, null);

            decision.Approved.Should().BeFalse();
            decision.Rejections.Should().HaveCount(2);
            decision.Rejections.Select(r => r.Reason).Should()
                .Contain(new[] { ImportRejectionReason.EmptyArchive, ImportRejectionReason.MinimumFreeSpace });
        }

        [Test]
        public void should_isolate_thrown_specs_with_decision_error_reason()
        {
            var goodSpec = new Mock<IMangaImportDecisionEngineSpecification>();
            goodSpec.Setup(s => s.IsSatisfiedBy(It.IsAny<LocalChapter>(), It.IsAny<DownloadClientItem>()))
                    .Returns(MangaImportSpecDecision.Accept());
            var brokenSpec = new Mock<IMangaImportDecisionEngineSpecification>();
            brokenSpec.Setup(s => s.IsSatisfiedBy(It.IsAny<LocalChapter>(), It.IsAny<DownloadClientItem>()))
                      .Throws(new System.InvalidOperationException("boom"));

            var subject = Make(goodSpec.Object, brokenSpec.Object);

            var decision = subject.GetDecision(_localChapter, null);

            decision.Approved.Should().BeFalse();
            decision.Rejections.Should().ContainSingle(r => r.Reason == ImportRejectionReason.DecisionError);

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_batch_decisions_for_all_local_chapters()
        {
            var spec = new Mock<IMangaImportDecisionEngineSpecification>();
            spec.Setup(s => s.IsSatisfiedBy(It.IsAny<LocalChapter>(), It.IsAny<DownloadClientItem>()))
                .Returns(MangaImportSpecDecision.Accept());

            var subject = Make(spec.Object);

            var decisions = subject.GetImportDecisions(
                new List<LocalChapter> { _localChapter, _localChapter },
                null);

            decisions.Should().HaveCount(2);
            decisions.All(d => d.Approved).Should().BeTrue();
            spec.Verify(s => s.IsSatisfiedBy(It.IsAny<LocalChapter>(), It.IsAny<DownloadClientItem>()), Times.Exactly(2));
        }
    }
}
