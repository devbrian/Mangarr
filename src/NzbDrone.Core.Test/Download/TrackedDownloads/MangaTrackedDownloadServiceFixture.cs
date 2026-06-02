using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History.Manga;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Test.Download.Manga.Builders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.TrackedDownloads
{
    // Phase 36 Plan 36-03 (LOOP-02) — CONTRACT tests for the HIGHEST-RISK matcher/registry. These
    // assert observable behavior (which path fired, what RemoteChapter resolved, the page-channel
    // null-clean property), NOT internal structure (Pitfall 1: implementation tests mask divergence).
    //
    // Seven locked behaviors:
    //   1. DownloadId-PRIMARY hit — GetLatestGrab Times.Once, title-parse NOT invoked.
    //   2. FALLBACK Warn — a join miss logs a Warn and maps via the parsing service.
    //   3. FALLBACK-null — GetManga null yields an ImportBlocked shell (no throw — anti-pattern D).
    //   4. MULTI-CHAPTER — ChapterIds [179,180,181] resolves 3 chapters (never collapsed).
    //   5. DECIMAL — chapter 12.5 round-trips as a decimal (no int truncation).
    //   6. LANGUAGE — es and es-la grabs resolve as distinct matches (not (mangaId, number) alone).
    //   7. PAGE CHANNEL — null-clean when no source reports the id (gateway path); populated when one does.
    //
    // The Subject is constructed by hand (NOT via Mocker.Resolve) so the IEnumerable<
    // IMangaDownloadPageProgressSource> set is fully controlled per test — the gateway path is the
    // empty set, the in-process path a single reporting source.
    [TestFixture]
    public class MangaTrackedDownloadServiceFixture : CoreTest
    {
        private Mock<IMangaDownloadHistoryService> _historyService;
        private Mock<IMangaParsingService> _parsingService;
        private Mock<IMangaService> _mangaService;
        private Mock<IChapterService> _chapterService;
        private List<IMangaDownloadPageProgressSource> _pageProgressSources;

        private NzbDrone.Core.Manga.Manga _manga;
        private DownloadClientDefinition _definition;

        [SetUp]
        public void Setup()
        {
            _historyService = new Mock<IMangaDownloadHistoryService>();
            _parsingService = new Mock<IMangaParsingService>();
            _mangaService = new Mock<IMangaService>();
            _chapterService = new Mock<IChapterService>();
            _pageProgressSources = new List<IMangaDownloadPageProgressSource>();

            _manga = new NzbDrone.Core.Manga.Manga { Id = 7, Title = "Test Manga" };
            _definition = new DownloadClientDefinition { Id = 1, Protocol = DownloadProtocol.Http };

            _mangaService.Setup(s => s.GetManga(7)).Returns(_manga);
        }

        private MangaTrackedDownloadService Subject()
        {
            return new MangaTrackedDownloadService(
                _historyService.Object,
                _parsingService.Object,
                _mangaService.Object,
                _chapterService.Object,
                _pageProgressSources,
                TestLogger);
        }

        private MangaDownloadHistory GrabRow(string downloadId, params int[] chapterIds)
        {
            return new MangaDownloadHistory
            {
                DownloadId = downloadId,
                MangaId = 7,
                ChapterIds = chapterIds.ToList(),
                EventType = MangaDownloadHistoryEventType.DownloadGrabbed,
                SourceTitle = "Test Manga - Chapter 001"
            };
        }

        private static DownloadClientItem Item(string downloadId, string title = "Test Manga - Chapter 001")
        {
            return new DownloadClientItemBuilder().WithDownloadId(downloadId).WithTitle(title).Build();
        }

        private void SetupChapters(params Chapter[] chapters)
        {
            _chapterService
                .Setup(s => s.GetChapters(It.IsAny<IEnumerable<int>>()))
                .Returns<IEnumerable<int>>(ids =>
                {
                    var idSet = ids.ToHashSet();
                    return chapters.Where(c => idSet.Contains(c.Id)).ToList();
                });
        }

        // (1) DownloadId-PRIMARY hit — GetLatestGrab once, title-parse never invoked.
        [Test]
        public void TrackDownload_with_grab_row_matches_via_DownloadId_and_skips_title_parse()
        {
            _historyService.Setup(s => s.GetLatestGrab("dl-1")).Returns(GrabRow("dl-1", 42));
            SetupChapters(new Chapter { Id = 42, MangaId = 7, ChapterNumber = 1m });

            var tracked = Subject().TrackDownload(_definition, Item("dl-1"));

            tracked.RemoteChapter.Should().NotBeNull();
            tracked.RemoteChapter.Manga.Id.Should().Be(7);
            tracked.RemoteChapter.Chapters.Should().ContainSingle(c => c.Id == 42);
            tracked.State.Should().Be(TrackedDownloadState.Downloading);

            _historyService.Verify(s => s.GetLatestGrab("dl-1"), Times.Once);
            _parsingService.Verify(s => s.GetManga(It.IsAny<string>()), Times.Never);
        }

        // (2) FALLBACK — a join miss logs a Warn and maps via the parsing service.
        [Test]
        public void TrackDownload_without_grab_row_logs_warn_and_falls_back_to_title_parse()
        {
            _historyService.Setup(s => s.GetLatestGrab("dl-miss")).Returns((MangaDownloadHistory)null);
            _parsingService.Setup(s => s.GetManga(It.IsAny<string>())).Returns(_manga);
            _chapterService.Setup(s => s.GetChaptersByManga(7)).Returns(new List<Chapter>());
            _parsingService
                .Setup(s => s.Map(It.IsAny<ParsedChapterInfo>(), _manga, It.IsAny<IList<Chapter>>()))
                .Returns(new RemoteChapter { Manga = _manga, Chapters = new List<Chapter>() });

            var tracked = Subject().TrackDownload(_definition, Item("dl-miss"));

            tracked.RemoteChapter.Should().NotBeNull();
            tracked.RemoteChapter.Manga.Id.Should().Be(7);

            _parsingService.Verify(s => s.GetManga(It.IsAny<string>()), Times.Once);
            ExceptionVerification.ExpectedWarns(1);
        }

        // (3) FALLBACK-null — GetManga null yields an ImportBlocked shell; never throws.
        [Test]
        public void TrackDownload_fallback_null_produces_ImportBlocked_shell_without_throwing()
        {
            _historyService.Setup(s => s.GetLatestGrab("dl-orphan")).Returns((MangaDownloadHistory)null);
            _parsingService.Setup(s => s.GetManga(It.IsAny<string>())).Returns((NzbDrone.Core.Manga.Manga)null);

            var tracked = Subject().TrackDownload(_definition, Item("dl-orphan"));

            tracked.Should().NotBeNull();
            tracked.RemoteChapter.Should().BeNull();
            tracked.State.Should().Be(TrackedDownloadState.ImportBlocked);
            tracked.IsTrackable.Should().BeTrue();

            ExceptionVerification.ExpectedWarns(1);
        }

        // (4) MULTI-CHAPTER — a pack resolves ALL chapter ids, never collapsed to one (Pitfall 2).
        [Test]
        public void TrackDownload_multi_chapter_pack_resolves_all_three_chapters()
        {
            _historyService.Setup(s => s.GetLatestGrab("dl-pack")).Returns(GrabRow("dl-pack", 179, 180, 181));
            SetupChapters(
                new Chapter { Id = 179, MangaId = 7, ChapterNumber = 179m },
                new Chapter { Id = 180, MangaId = 7, ChapterNumber = 180m },
                new Chapter { Id = 181, MangaId = 7, ChapterNumber = 181m });

            var tracked = Subject().TrackDownload(_definition, Item("dl-pack"));

            tracked.RemoteChapter.Chapters.Select(c => c.Id)
                .Should().BeEquivalentTo(new[] { 179, 180, 181 });
        }

        // (5) DECIMAL — chapter 12.5 round-trips as a decimal (no int truncation to 12).
        [Test]
        public void TrackDownload_decimal_chapter_preserves_fractional_number()
        {
            _historyService.Setup(s => s.GetLatestGrab("dl-dec")).Returns(GrabRow("dl-dec", 125));
            SetupChapters(new Chapter { Id = 125, MangaId = 7, ChapterNumber = 12.5m });

            var tracked = Subject().TrackDownload(_definition, Item("dl-dec"));

            tracked.RemoteChapter.Chapters.Should().ContainSingle();
            tracked.RemoteChapter.Chapters[0].ChapterNumber.Should().Be(12.5m);
        }

        // (6) LANGUAGE — es and es-la grabs of the same chapter NUMBER are distinct matches because
        // the match key is the DownloadId-keyed grab row (distinct ChapterIds), not (manga, number).
        [Test]
        public void TrackDownload_language_variants_resolve_as_distinct_matches()
        {
            _historyService.Setup(s => s.GetLatestGrab("dl-es")).Returns(GrabRow("dl-es", 500));
            _historyService.Setup(s => s.GetLatestGrab("dl-es-la")).Returns(GrabRow("dl-es-la", 501));
            SetupChapters(
                new Chapter { Id = 500, MangaId = 7, ChapterNumber = 7m },
                new Chapter { Id = 501, MangaId = 7, ChapterNumber = 7m });

            var es = Subject().TrackDownload(_definition, Item("dl-es"));
            var esLa = Subject().TrackDownload(_definition, Item("dl-es-la"));

            es.RemoteChapter.Chapters.Single().Id.Should().Be(500);
            esLa.RemoteChapter.Chapters.Single().Id.Should().Be(501);
            es.RemoteChapter.Chapters.Single().Id
                .Should().NotBe(esLa.RemoteChapter.Chapters.Single().Id);
        }

        // (CR-01) PROVENANCE ROUND-TRIP — drives the REAL grab-row writer
        // (MangaDownloadHistoryService.Handle(ChapterGrabbedEvent)) to build the Data dictionary, then
        // feeds that row into the REAL MapFromHistory projection and asserts ScanlationGroup /
        // TranslatedLanguage / Indexer are ALL non-blank on the resulting RemoteChapter.Release. This
        // exercises the real chain (writer → reader), unlike the MangaCompletedDownloadServiceFixture
        // [SetUp] which hand-set the release fields and masked the dropped provenance.
        [Test]
        public void TrackDownload_primary_path_carries_scanlation_group_language_and_indexer_provenance()
        {
            // Build the grab row exactly as the production writer does — run the REAL
            // MangaDownloadHistoryService.Handle(ChapterGrabbedEvent) and capture the inserted row so
            // its Data dictionary reflects the production key casing.
            var historyRepo = new Mock<IMangaDownloadHistoryRepository>();
            MangaDownloadHistory inserted = null;
            historyRepo.Setup(r => r.Insert(It.IsAny<MangaDownloadHistory>()))
                .Callback<MangaDownloadHistory>(h => inserted = h)
                .Returns<MangaDownloadHistory>(h => h);

            var historyService = new MangaDownloadHistoryService(historyRepo.Object, TestLogger);

            var grabbedRemote = new RemoteChapter
            {
                Manga = _manga,
                Chapters = new List<Chapter> { new() { Id = 42, MangaId = 7, ChapterNumber = 1m } },
                Release = new NzbDrone.Core.Parser.Model.ReleaseInfo
                {
                    Title = "Test Manga - Chapter 001",
                    Indexer = "MangaDex",
                    ScanlationGroup = "Acme Scans",
                    TranslatedLanguage = "en"
                }
            };

            historyService.Handle(new NzbDrone.Core.MediaFiles.ChapterArchiving.ChapterGrabbedEvent(
                grabbedRemote, "dl-prov", "InProcess"));

            inserted.Should().NotBeNull("the grab handler must insert the join row");

            // Feed the REAL inserted row back through the matcher's primary path.
            _historyService.Setup(s => s.GetLatestGrab("dl-prov")).Returns(inserted);
            SetupChapters(new Chapter { Id = 42, MangaId = 7, ChapterNumber = 1m });

            var tracked = Subject().TrackDownload(_definition, Item("dl-prov"));

            tracked.RemoteChapter.Should().NotBeNull();
            tracked.RemoteChapter.Release.Should().NotBeNull();
            tracked.RemoteChapter.Release.ScanlationGroup.Should().Be("Acme Scans",
                "CR-01: ScanlationGroup must round-trip through the grab row's Data dictionary");
            tracked.RemoteChapter.Release.TranslatedLanguage.Should().Be("en",
                "CR-01: TranslatedLanguage must round-trip through the grab row's Data dictionary");
            tracked.RemoteChapter.Release.Indexer.Should().Be("MangaDex",
                "CR-01: Indexer must round-trip (the old writer/reader key-casing mismatch dropped it)");
            tracked.Indexer.Should().Be("MangaDex");
        }

        // (7a) PAGE CHANNEL — gateway path (no source) returns null. The shared DownloadClientItem
        // is never touched and ChapterDownloadState is never read.
        [Test]
        public void GetPageProgress_returns_null_on_gateway_path_with_no_source()
        {
            _pageProgressSources.Clear();

            Subject().GetPageProgress("anything").Should().BeNull();
        }

        // (7b) PAGE CHANNEL — in-process path (a reporting source) surfaces the counts.
        [Test]
        public void GetPageProgress_returns_counts_from_reporting_source()
        {
            var source = new Mock<IMangaDownloadPageProgressSource>();
            source.Setup(s => s.GetPageProgress("dl-pages")).Returns(new MangaDownloadPageProgress(20, 7));
            source.Setup(s => s.GetPageProgress(It.Is<string>(id => id != "dl-pages"))).Returns((MangaDownloadPageProgress)null);
            _pageProgressSources.Add(source.Object);

            var progress = Subject().GetPageProgress("dl-pages");

            progress.Should().NotBeNull();
            progress.TotalPages.Should().Be(20);
            progress.CompletedPages.Should().Be(7);

            Subject().GetPageProgress("dl-other").Should().BeNull();
        }
    }
}
