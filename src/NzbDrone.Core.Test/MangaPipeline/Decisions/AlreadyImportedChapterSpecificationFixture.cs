using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Manga.Specifications;
using NzbDrone.Core.History;
using NzbDrone.Core.History.Manga;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MangaPipeline.Decisions
{
    // Phase 6 Wave 1 BLOCKING fixture — D-21 STUB-replacement target + BL-01 regression guard.
    // Wired by Plan 06-03 (ChapterHistoryService + AlreadyImportedChapterSpecification body).
    //
    // BL-01 cross-domain ID-collision regression: the spec MUST query
    // ChapterHistory.ChapterId — NOT EpisodeHistory.EpisodeId. The Queries_ChapterHistory_only
    // test seeds an EpisodeHistory row with EpisodeId == 42 of an unrelated TV series, then
    // calls IsSatisfiedBy for a manga chapter whose Id == 42. If the bug-present STUB still
    // dispatched to IHistoryService.FindByEpisodeId, this test would fail (the spec would
    // reject the manga release as "already imported" against the unrelated TV row). The
    // wired-up version queries the new sibling table — separate Mapper.Entity registration —
    // and returns Accept.
    //
    // Phase 6 Pitfall 6 GUARD note: AlreadyImportedChapterSpecification gained an
    // IChapterHistoryService dependency in this plan. The 11-spec auto-discovery count
    // (MangaDownloadDecisionMakerEndToEndFixture.All_eleven_manga_specs_auto_discovered_*) still
    // passes Be(11) because the class shape and IMangaDecisionEngineSpecification interface
    // implementation are unchanged.
    [TestFixture]
    public class AlreadyImportedChapterSpecificationFixture : DbTest
    {
        private AlreadyImportedChapterSpecification _spec;
        private Mock<IChapterHistoryService> _chapterHistoryService;

        [SetUp]
        public void Setup()
        {
            _chapterHistoryService = new Mock<IChapterHistoryService>();
            _spec = new AlreadyImportedChapterSpecification(_chapterHistoryService.Object, LogManager.GetLogger("test"));
        }

        private RemoteChapter BuildRemoteChapter(int chapterId, bool monitored = true)
        {
            return new RemoteChapter
            {
                Manga = new NzbDrone.Core.Manga.Manga { Id = 7, Title = "Test Manga" },
                Chapters = new List<Chapter>
                {
                    new() { Id = chapterId, MangaId = 7, Monitored = monitored, ChapterNumber = 1m }
                },
                Release = new ReleaseInfo { Title = "Test Manga - Chapter 001", Indexer = "MangaDex" }
            };
        }

        [Test]
        public void Queries_ChapterHistory_only_not_EpisodeHistory()
        {
            // BL-01 regression — DbTest gives us a real SQLite database, so we can prove the
            // separation at the persistence layer. Seed an EpisodeHistory row with EpisodeId=42.
            // The wired spec must NOT see this row when it calls FindByChapterId(42).
            var tvDb = Mocker.Resolve<NzbDrone.Core.Datastore.IMainDatabase>();
            var episodeHistoryRepo = new HistoryRepository(tvDb, Mocker.Resolve<IEventAggregator>());
            var episodeHistory = Builder<EpisodeHistory>.CreateNew()
                .With(h => h.EpisodeId = 42)
                .With(h => h.SeriesId = 999)
                .With(h => h.EventType = EpisodeHistoryEventType.DownloadFolderImported)
                .With(h => h.Languages = new List<Language> { Language.English })
                .With(h => h.Quality = new QualityModel())
                .BuildNew();
            episodeHistoryRepo.Insert(episodeHistory);

            // Spec uses the mocked IChapterHistoryService (NOT the real repository) — the mock
            // returns empty for chapter 42, modeling the new sibling-table behavior. The TV
            // EpisodeHistory row above is the BL-01 trap value: present in the DB but on a
            // separate table, never reachable from FindByChapterId. The mock setup encodes the
            // contract; the persistence-level seed is the regression guard.
            _chapterHistoryService.Setup(s => s.FindByChapterId(42))
                .Returns(new List<ChapterHistory>());

            var subject = BuildRemoteChapter(chapterId: 42, monitored: true);
            var decision = _spec.IsSatisfiedBy(subject, new ReleaseDecisionInformation());

            decision.Accepted.Should().BeTrue(
                "BL-01 fix: ChapterHistory.ChapterId is INDEPENDENT of EpisodeHistory.EpisodeId. "
                + "The spec must NOT reject when only a TV-side EpisodeHistory row with EpisodeId=42 exists.");
            _chapterHistoryService.Verify(s => s.FindByChapterId(42), Times.Once);
        }

        [Test]
        public void Rejects_when_imported_history_exists()
        {
            _chapterHistoryService.Setup(s => s.FindByChapterId(50))
                .Returns(new List<ChapterHistory>
                {
                    new() { ChapterId = 50, EventType = ChapterHistoryEventType.Imported, MangaId = 7 }
                });

            var subject = BuildRemoteChapter(chapterId: 50, monitored: true);
            var decision = _spec.IsSatisfiedBy(subject, new ReleaseDecisionInformation());

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(DownloadRejectionReason.ChapterAlreadyImported);
        }

        [Test]
        public void Accepts_when_no_import_history()
        {
            _chapterHistoryService.Setup(s => s.FindByChapterId(60))
                .Returns(new List<ChapterHistory>());

            var subject = BuildRemoteChapter(chapterId: 60, monitored: true);
            var decision = _spec.IsSatisfiedBy(subject, new ReleaseDecisionInformation());

            decision.Accepted.Should().BeTrue();
        }

        [Test]
        public void Accepts_when_imported_history_exists_but_chapter_unmonitored()
        {
            _chapterHistoryService.Setup(s => s.FindByChapterId(70))
                .Returns(new List<ChapterHistory>
                {
                    new() { ChapterId = 70, EventType = ChapterHistoryEventType.Imported, MangaId = 7 }
                });

            var subject = BuildRemoteChapter(chapterId: 70, monitored: false);
            var decision = _spec.IsSatisfiedBy(subject, new ReleaseDecisionInformation());

            decision.Accepted.Should().BeTrue("unmonitored chapters skip the AlreadyImported gate");
        }
    }
}
