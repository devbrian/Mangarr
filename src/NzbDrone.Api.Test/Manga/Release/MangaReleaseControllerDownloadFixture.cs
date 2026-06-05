using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using FizzWare.NBuilder;
using FluentAssertions;
using Mangarr.Api.V5.Manga.Release;
using Microsoft.AspNetCore.Http.HttpResults;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.DecisionEngine.Manga;
using NzbDrone.Core.Download;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Test.Common;

namespace NzbDrone.Api.Test.Manga.Release
{
    // Phase 40 Plan 40-01 fixture (RECON-01 / D-10): proves MangaReleaseController.DownloadRelease
    // surfaces a silent grab failure as a 404 instead of greening the UI button. The controller
    // previously DISCARDED the ProcessedDecisionResult returned by ProcessDecision and always
    // returned 200 — so a Rejected/Skipped decision queued nothing yet told the user it succeeded.
    //
    // Fixture lives under NzbDrone.Api.Test rather than NzbDrone.Core.Test because Mangarr.Core.Test
    // does not project-reference Mangarr.Api.V5; Mangarr.Api.Test does. Same Mocker / TestBase<TSubject>
    // harness; only the project boundary changes. Convention established Plan 10-05 — see
    // MangaControllerSignalRFixture.cs:23-27.
    //
    // DownloadRelease path: the ctor resolves ICacheManager.GetCache<RemoteChapter>(GetType(),
    // "remoteChapters"); the method then calls _remoteChapterCache.Find(key). Setup wires a mock
    // ICached<RemoteChapter> whose Find returns a Builder-built non-null RemoteChapter so the method
    // reaches ProcessDecision. Each test flips the IProcessMangaDownloadDecisions.ProcessDecision
    // return value.
    //
    // Phase 40 Plan 40-04 extension (RECON-03 / D-04): DownloadRelease now calls
    // IChapterSynthesisService.SynthesizeForGrab(remoteChapter) BEFORE building the decision, so a
    // manual grab of an uncataloged chapter synthesizes-then-downloads. The new tests assert the
    // hook fires Times.Once and BEFORE ProcessDecision (invocation-order capture), and confirm the
    // Part-A 404 backstop still fires on a Rejected decision after synthesis ran.
    [TestFixture]
    public class MangaReleaseControllerDownloadFixture : TestBase<MangaReleaseController>
    {
        private Mock<ICached<RemoteChapter>> _cache;
        private RemoteChapter _remoteChapter;
        private List<string> _invocationOrder;

        [SetUp]
        public void Setup()
        {
            _remoteChapter = Builder<RemoteChapter>.CreateNew().Build();
            _invocationOrder = new List<string>();

            // The controller resolves cacheManager.GetCache<RemoteChapter>(GetType(), "remoteChapters")
            // in its ctor (MangaReleaseController.cs:65); return a mock cache whose Find yields the
            // built RemoteChapter so DownloadRelease reaches ProcessDecision.
            _cache = new Mock<ICached<RemoteChapter>>();
            _cache.Setup(c => c.Find(It.IsAny<string>())).Returns(_remoteChapter);

            Mocker.GetMock<ICacheManager>()
                  .Setup(m => m.GetCache<RemoteChapter>(It.IsAny<Type>(), It.IsAny<string>()))
                  .Returns(_cache.Object);

            // Capture invocation order: SynthesizeForGrab must run BEFORE ProcessDecision (D-04).
            // Returns the resolved Chapter rows (WR-01) — default empty here; individual tests
            // override to assert the controller re-hydrates RemoteChapter.Chapters.
            Mocker.GetMock<IChapterSynthesisService>()
                  .Setup(s => s.SynthesizeForGrab(It.IsAny<RemoteChapter>()))
                  .Callback(() => _invocationOrder.Add("SynthesizeForGrab"))
                  .Returns(new List<NzbDrone.Core.Manga.Chapter>());

            // Default: a healthy grab. Individual tests override the return value.
            Mocker.GetMock<IProcessMangaDownloadDecisions>()
                  .Setup(s => s.ProcessDecision(It.IsAny<MangaDownloadDecision>(), It.IsAny<int?>()))
                  .Callback(() => _invocationOrder.Add("ProcessDecision"))
                  .ReturnsAsync(ProcessedDecisionResult.Grabbed);
        }

        private static MangaReleaseResource BuildResource()
        {
            return new MangaReleaseResource
            {
                Guid = "release-guid",
                IndexerId = 1
            };
        }

        // Test A — Rejected decision must surface as a 404 (D-10), not a silent 200.
        [Test]
        public void DownloadRelease_should_throw_NotFound_when_decision_is_Rejected()
        {
            Mocker.GetMock<IProcessMangaDownloadDecisions>()
                  .Setup(s => s.ProcessDecision(It.IsAny<MangaDownloadDecision>(), It.IsAny<int?>()))
                  .ReturnsAsync(ProcessedDecisionResult.Rejected);

            var ex = Assert.ThrowsAsync<NzbDroneClientException>(
                async () => await Subject.DownloadRelease(BuildResource()));

            ex.StatusCode.Should().Be(HttpStatusCode.NotFound);

            // The D-10 guard logs a Warn before throwing; acknowledge it so the TestBase
            // unexpected-log assertion passes.
            ExceptionVerification.ExpectedWarns(1);
        }

        // Test B — Skipped decision must surface as a 404 (D-10).
        [Test]
        public void DownloadRelease_should_throw_NotFound_when_decision_is_Skipped()
        {
            Mocker.GetMock<IProcessMangaDownloadDecisions>()
                  .Setup(s => s.ProcessDecision(It.IsAny<MangaDownloadDecision>(), It.IsAny<int?>()))
                  .ReturnsAsync(ProcessedDecisionResult.Skipped);

            var ex = Assert.ThrowsAsync<NzbDroneClientException>(
                async () => await Subject.DownloadRelease(BuildResource()));

            ex.StatusCode.Should().Be(HttpStatusCode.NotFound);

            // The D-10 guard logs a Warn before throwing; acknowledge it so the TestBase
            // unexpected-log assertion passes.
            ExceptionVerification.ExpectedWarns(1);
        }

        // Test C — Grabbed decision is the unchanged happy path: 200 with the release resource.
        [Test]
        public async Task DownloadRelease_should_return_Ok_when_decision_is_Grabbed()
        {
            Mocker.GetMock<IProcessMangaDownloadDecisions>()
                  .Setup(s => s.ProcessDecision(It.IsAny<MangaDownloadDecision>(), It.IsAny<int?>()))
                  .ReturnsAsync(ProcessedDecisionResult.Grabbed);

            var resource = BuildResource();
            var result = await Subject.DownloadRelease(resource);

            var ok = result.Result as Ok<MangaReleaseResource>;
            ok.Should().NotBeNull();
            ok!.Value.Should().BeSameAs(resource);
        }

        // Test D — cache-miss regression guard: the pre-existing NotFound throw still fires when
        // the cached RemoteChapter is absent (proves the new guard did not disturb that path).
        [Test]
        public void DownloadRelease_should_throw_NotFound_when_release_not_in_cache()
        {
            _cache.Setup(c => c.Find(It.IsAny<string>())).Returns((RemoteChapter)null);

            var ex = Assert.ThrowsAsync<NzbDroneClientException>(
                async () => await Subject.DownloadRelease(BuildResource()));

            ex.StatusCode.Should().Be(HttpStatusCode.NotFound);

            // Cache-miss short-circuits before synthesis — the on-grab hook never runs.
            Mocker.GetMock<IChapterSynthesisService>()
                  .Verify(s => s.SynthesizeForGrab(It.IsAny<RemoteChapter>()), Times.Never);
        }

        // Test E — RECON-03 / D-04: on a healthy grab the on-grab synthesis hook runs exactly once.
        [Test]
        public async Task DownloadRelease_should_invoke_SynthesizeForGrab_once()
        {
            await Subject.DownloadRelease(BuildResource());

            Mocker.GetMock<IChapterSynthesisService>()
                  .Verify(s => s.SynthesizeForGrab(_remoteChapter), Times.Once);
        }

        // Test F — D-04 ordering: SynthesizeForGrab MUST run BEFORE ProcessDecision so the
        // freshly-synthesized Chapter row makes the decision qualify.
        [Test]
        public async Task DownloadRelease_should_invoke_SynthesizeForGrab_before_ProcessDecision()
        {
            await Subject.DownloadRelease(BuildResource());

            _invocationOrder.Should().ContainInOrder("SynthesizeForGrab", "ProcessDecision");
            _invocationOrder.IndexOf("SynthesizeForGrab")
                .Should().BeLessThan(_invocationOrder.IndexOf("ProcessDecision"));
        }

        // Test G — belt-and-suspenders (D-11): even after on-grab synthesis runs, a Rejected
        // decision still surfaces as the Part-A 404 backstop.
        [Test]
        public void DownloadRelease_should_still_404_on_Rejected_after_synthesis()
        {
            Mocker.GetMock<IProcessMangaDownloadDecisions>()
                  .Setup(s => s.ProcessDecision(It.IsAny<MangaDownloadDecision>(), It.IsAny<int?>()))
                  .ReturnsAsync(ProcessedDecisionResult.Rejected);

            var ex = Assert.ThrowsAsync<NzbDroneClientException>(
                async () => await Subject.DownloadRelease(BuildResource()));

            ex.StatusCode.Should().Be(HttpStatusCode.NotFound);

            Mocker.GetMock<IChapterSynthesisService>()
                  .Verify(s => s.SynthesizeForGrab(_remoteChapter), Times.Once);

            // The D-10 guard logs a Warn before throwing; acknowledge it.
            ExceptionVerification.ExpectedWarns(1);
        }

        // Test H (WR-01) — the controller re-hydrates RemoteChapter.Chapters from the rows
        // SynthesizeForGrab returns, so a just-synthesized uncataloged chapter qualifies the
        // decision (the cached RemoteChapter had no row for it at search time).
        [Test]
        public async Task DownloadRelease_should_rehydrate_chapters_from_synthesis_result()
        {
            _remoteChapter.Chapters = new List<NzbDrone.Core.Manga.Chapter>();
            var synthesized = new NzbDrone.Core.Manga.Chapter { Id = 4242, ChapterNumber = 24m };

            Mocker.GetMock<IChapterSynthesisService>()
                  .Setup(s => s.SynthesizeForGrab(It.IsAny<RemoteChapter>()))
                  .Returns(new List<NzbDrone.Core.Manga.Chapter> { synthesized });

            await Subject.DownloadRelease(BuildResource());

            _remoteChapter.Chapters.Should().ContainSingle(c => c.Id == 4242);
        }

        // Test I (WR-03) — synthesis is best-effort: a throw from SynthesizeForGrab must NOT
        // blow up an otherwise-grabbable release. The grab proceeds and returns 200.
        [Test]
        public async Task DownloadRelease_should_continue_when_synthesis_throws()
        {
            Mocker.GetMock<IChapterSynthesisService>()
                  .Setup(s => s.SynthesizeForGrab(It.IsAny<RemoteChapter>()))
                  .Throws(new InvalidOperationException("synthesis blew up"));

            var result = await Subject.DownloadRelease(BuildResource());

            result.Result.Should().BeOfType<Ok<MangaReleaseResource>>();

            // The WR-03 guard logs a Warn on the swallowed synthesis failure; acknowledge it.
            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
