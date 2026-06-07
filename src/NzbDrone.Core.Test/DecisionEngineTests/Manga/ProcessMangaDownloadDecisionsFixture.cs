using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Manga;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Download.Pending.Manga;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Translations;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.DecisionEngineTests.Manga
{
    // Phase 8 Plan 99-06 — closes the search→grab regression. ProcessMangaDownloadDecisions
    // mirrors TV ProcessDownloadDecisions: walks ranked decisions, grabs approved ones via
    // IMangaDownloadService.DownloadReport (Phase 15 Wave (A) W-4 rebind 2026-05-07;
    // pre-Wave-(A) used the RemoteChapter.ToRemoteEpisodeShim() bridge into IDownloadService),
    // routes pending / failed / rejected into the appropriate buckets.
    [TestFixture]
    public class ProcessMangaDownloadDecisionsFixture : CoreTest<ProcessMangaDownloadDecisions>
    {
        private MangaDownloadDecision BuildApproved(int chapterId)
        {
            return new MangaDownloadDecision(BuildRemoteChapter(chapterId));
        }

        private MangaDownloadDecision BuildTemporarilyRejected(int chapterId)
        {
            return new MangaDownloadDecision(BuildRemoteChapter(chapterId),
                new DownloadRejection(DownloadRejectionReason.MinimumAge, "too new", RejectionType.Temporary));
        }

        private MangaDownloadDecision BuildPermanentlyRejected(int chapterId)
        {
            return new MangaDownloadDecision(BuildRemoteChapter(chapterId),
                new DownloadRejection(DownloadRejectionReason.EpisodeNotMonitored, "unmonitored"));
        }

        private RemoteChapter BuildRemoteChapter(int chapterId)
        {
            return new RemoteChapter
            {
                Manga = new NzbDrone.Core.Manga.Manga { Id = 1, Title = "Test Manga" },
                Chapters = new List<Chapter>
                {
                    new() { Id = chapterId, ChapterNumber = chapterId }
                },
                Release = new ReleaseInfo
                {
                    Title = $"Chapter {chapterId}",
                    Indexer = "TestIndexer",
                    DownloadProtocol = DownloadProtocol.Http
                }
            };
        }

        [SetUp]
        public void SetUp()
        {
            // Provide a real comparer with mocked deps — AutoMoq cannot construct it because
            // its ctor takes non-mockable concrete prerequisites we want to control here.
            Mocker.SetConstant(new MangaDownloadDecisionComparer(
                Mocker.GetMock<ITranslationProfileService>().Object,
                Mocker.GetMock<IConfigService>().Object));

            // Default: treat any prioritization request as a no-op pass-through (the comparer
            // would otherwise call into the mocked ITranslationProfileService and yield 0/empty
            // for missing profiles — which is fine; we don't care about ordering for these tests).
        }

        [Test]
        public async Task Should_return_empty_grabbed_when_all_decisions_rejected()
        {
            var decisions = new List<MangaDownloadDecision>
            {
                BuildPermanentlyRejected(1),
                BuildPermanentlyRejected(2)
            };

            var result = await Subject.ProcessDecisions(decisions);

            result.Grabbed.Should().BeEmpty();
            result.Rejected.Should().HaveCount(2);
            Mocker.GetMock<IMangaDownloadService>()
                .Verify(d => d.DownloadReport(It.IsAny<RemoteChapter>(), It.IsAny<int?>()), Times.Never);
        }

        [Test]
        public async Task Should_grab_via_download_service_when_approved()
        {
            var decisions = new List<MangaDownloadDecision> { BuildApproved(10) };

            var result = await Subject.ProcessDecisions(decisions);

            result.Grabbed.Should().HaveCount(1);
            Mocker.GetMock<IMangaDownloadService>()
                .Verify(d => d.DownloadReport(It.IsAny<RemoteChapter>(), null), Times.Once);
        }

        [Test]
        public async Task Should_grab_each_approved_when_chapters_are_distinct()
        {
            var decisions = new List<MangaDownloadDecision>
            {
                BuildApproved(11),
                BuildApproved(12),
                BuildApproved(13)
            };

            var result = await Subject.ProcessDecisions(decisions);

            result.Grabbed.Should().HaveCount(3);
            Mocker.GetMock<IMangaDownloadService>()
                .Verify(d => d.DownloadReport(It.IsAny<RemoteChapter>(), null), Times.Exactly(3));
        }

        // Regression guard (quick-260607-cto): ProcessMangaDownloadDecisions must consume the
        // comparer via OrderByDescending and grab the BEST-ranked candidate for a chapter. Two
        // candidates for the SAME chapter; the HIGHER CustomFormatScore must win. The LOWER-CF
        // candidate is listed FIRST on purpose so the test fails on BOTH failure modes: the
        // OrderBy-ascending inversion this fix removed AND a degenerate no-op/pass-through sort
        // (either would leave the worse candidate first and grab it).
        [Test]
        public async Task Should_grab_best_ranked_candidate_for_same_chapter_higher_custom_format_score_wins()
        {
            var high = new MangaDownloadDecision(BuildRemoteChapter(100));
            high.RemoteChapter.CustomFormatScore = 10;

            var low = new MangaDownloadDecision(BuildRemoteChapter(100));
            low.RemoteChapter.CustomFormatScore = 0;

            // Lower-CF candidate listed FIRST on purpose (see comment above): ordering logic must
            // actively promote the better candidate, so a pass-through sort can no longer pass.
            var decisions = new List<MangaDownloadDecision> { low, high };

            var result = await Subject.ProcessDecisions(decisions);

            result.Grabbed.Should().HaveCount(1);
            result.Grabbed[0].RemoteChapter.CustomFormatScore.Should().Be(10,
                "the higher custom-format-score candidate must be grabbed for a given chapter");
        }

        [Test]
        public async Task Should_route_temporarily_rejected_to_pending_with_delay_reason()
        {
            var decisions = new List<MangaDownloadDecision> { BuildTemporarilyRejected(20) };

            var result = await Subject.ProcessDecisions(decisions);

            result.Pending.Should().HaveCount(1);
            result.Grabbed.Should().BeEmpty();
            Mocker.GetMock<IMangaPendingReleaseService>()
                .Verify(
                    p => p.AddMany(It.Is<List<Tuple<MangaDownloadDecision, PendingReleaseReason>>>(
                        list => list.Count == 1 && list[0].Item2 == PendingReleaseReason.Delay)),
                    Times.Once);
        }

        [Test]
        public async Task Should_route_DownloadClientUnavailableException_to_pending_with_unavailable_reason()
        {
            var decisions = new List<MangaDownloadDecision> { BuildApproved(30) };

            Mocker.GetMock<IMangaDownloadService>()
                .Setup(d => d.DownloadReport(It.IsAny<RemoteChapter>(), It.IsAny<int?>()))
                .ThrowsAsync(new DownloadClientUnavailableException("client offline"));

            var result = await Subject.ProcessDecisions(decisions);

            result.Pending.Should().HaveCount(1);
            result.Grabbed.Should().BeEmpty();
            Mocker.GetMock<IMangaPendingReleaseService>()
                .Verify(
                    p => p.AddMany(It.Is<List<Tuple<MangaDownloadDecision, PendingReleaseReason>>>(
                        list => list.Count == 1 && list[0].Item2 == PendingReleaseReason.DownloadClientUnavailable)),
                    Times.Once);
        }

        [Test]
        public async Task Should_route_ReleaseUnavailableException_to_rejected()
        {
            var decisions = new List<MangaDownloadDecision> { BuildApproved(40) };

            Mocker.GetMock<IMangaDownloadService>()
                .Setup(d => d.DownloadReport(It.IsAny<RemoteChapter>(), It.IsAny<int?>()))
                .ThrowsAsync(new ReleaseUnavailableException(decisions[0].RemoteChapter.Release, "gone"));

            var result = await Subject.ProcessDecisions(decisions);

            // ProcessDecisionInternal logs at Warn on ReleaseUnavailableException; flush + ignore
            // before TearDown runs AssertNoUnexpectedLogs.
            NLog.LogManager.Flush();
            ExceptionVerification.IgnoreWarns();

            result.Grabbed.Should().BeEmpty();
            result.Rejected.Should().HaveCount(1);
        }

        // F-02 fix coverage — ProcessDecision singular overload (sonarr-consistency-audit
        // 2026-05-06). Mirrors TV ProcessDownloadDecisionsFixture's ProcessDecision tests.

        [Test]
        public async Task ProcessDecision_returns_Skipped_for_null_decision()
        {
            var result = await Subject.ProcessDecision(null, downloadClientId: null);
            result.Should().Be(ProcessedDecisionResult.Skipped);
        }

        [Test]
        public async Task ProcessDecision_returns_Pending_for_temporarily_rejected_decision()
        {
            var decision = BuildTemporarilyRejected(50);

            var result = await Subject.ProcessDecision(decision, downloadClientId: null);

            result.Should().Be(ProcessedDecisionResult.Pending);
            Mocker.GetMock<IMangaPendingReleaseService>()
                .Verify(p => p.Add(decision, PendingReleaseReason.Delay), Times.Once);
        }

        [Test]
        public async Task ProcessDecision_returns_Grabbed_for_approved_decision()
        {
            var decision = BuildApproved(60);

            var result = await Subject.ProcessDecision(decision, downloadClientId: 7);

            result.Should().Be(ProcessedDecisionResult.Grabbed);
            Mocker.GetMock<IMangaDownloadService>()
                .Verify(d => d.DownloadReport(It.IsAny<RemoteChapter>(), 7), Times.Once);
        }
    }
}
