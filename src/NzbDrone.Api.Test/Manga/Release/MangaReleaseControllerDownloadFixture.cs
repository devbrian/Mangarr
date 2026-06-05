using System;
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
    [TestFixture]
    public class MangaReleaseControllerDownloadFixture : TestBase<MangaReleaseController>
    {
        private Mock<ICached<RemoteChapter>> _cache;
        private RemoteChapter _remoteChapter;

        [SetUp]
        public void Setup()
        {
            _remoteChapter = Builder<RemoteChapter>.CreateNew().Build();

            // The controller resolves cacheManager.GetCache<RemoteChapter>(GetType(), "remoteChapters")
            // in its ctor (MangaReleaseController.cs:65); return a mock cache whose Find yields the
            // built RemoteChapter so DownloadRelease reaches ProcessDecision.
            _cache = new Mock<ICached<RemoteChapter>>();
            _cache.Setup(c => c.Find(It.IsAny<string>())).Returns(_remoteChapter);

            Mocker.GetMock<ICacheManager>()
                  .Setup(m => m.GetCache<RemoteChapter>(It.IsAny<Type>(), It.IsAny<string>()))
                  .Returns(_cache.Object);

            // Default: a healthy grab. Individual tests override the return value.
            Mocker.GetMock<IProcessMangaDownloadDecisions>()
                  .Setup(s => s.ProcessDecision(It.IsAny<MangaDownloadDecision>(), It.IsAny<int?>()))
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
        }
    }
}
