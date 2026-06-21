// Phase 40 Plan 40-04 (RECON-02 / D-01): unit coverage for the on-search synthesis hook
// wired into MangaReleaseSearchService.MangaSearch. After the decision maker runs, the
// service calls IChapterSynthesisService.SynthesizeFromDecisions(criteria.Manga, decisions)
// as a pure side-effect — the genuinely-missing WHOLE-number range is mirrored into the
// local Chapter catalog so RSS/missing search can discover it. The returned decision list
// is NOT mutated by synthesis.
//
// Behavior under test:
//   * SynthesizeFromDecisions is invoked exactly once per MangaSearch, after GetSearchDecision.
//   * The returned list equals the decision-maker output (synthesis did not mutate the return).
//   * ChapterSearch does NOT invoke synthesis (the chapter-scoped path relies on the on-grab
//     hook per RESEARCH Open Question #1).
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine.Manga;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.IndexerSearch.Manga;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

// Namespace deliberately uses the parent IndexerSearch scope (NOT a child .Manga
// namespace): a child NzbDrone.Core.Test.IndexerSearch.Manga namespace would shadow
// the bare `Manga.` token and break the sibling MangaSearchCriteriaFixture's
// `new Manga.Manga {...}` references (C# nearest-enclosing-namespace lookup).
namespace NzbDrone.Core.Test.IndexerSearch
{
    [TestFixture]
    public class MangaReleaseSearchServiceFixture : CoreTest<MangaReleaseSearchService>
    {
        private Core.Manga.Manga _manga;
        private List<MangaDownloadDecision> _decisions;

        [SetUp]
        public void Setup()
        {
            _manga = new Core.Manga.Manga { Id = 7, Title = "The Forgotten Field" };

            _decisions = new List<MangaDownloadDecision>
            {
                new MangaDownloadDecision(new RemoteChapter { Release = new ReleaseInfo { Title = "Chapter 1" } }),
                new MangaDownloadDecision(new RemoteChapter { Release = new ReleaseInfo { Title = "Chapter 2" } })
            };

            // Provide one Http-protocol indexer whose Fetch returns an empty report list. A
            // non-empty enabled-indexer set avoids the "No manga indexers available" Warn that
            // would otherwise trip TestBase's unexpected-log assertion; the decision maker is
            // mocked to return the canned decisions regardless of the (empty) report set.
            var indexer = new Mock<IIndexer>();
            indexer.SetupGet(i => i.Protocol).Returns(DownloadProtocol.Http);
            indexer.Setup(i => i.Definition).Returns(new IndexerDefinition { Name = "TestGateway" });
            indexer.Setup(i => i.Fetch(It.IsAny<MangaSearchCriteria>()))
                   .ReturnsAsync((IList<ReleaseInfo>)new List<ReleaseInfo>());
            indexer.Setup(i => i.Fetch(It.IsAny<ChapterSearchCriteria>()))
                   .ReturnsAsync((IList<ReleaseInfo>)new List<ReleaseInfo>());

            Mocker.GetMock<IIndexerFactory>()
                  .Setup(f => f.AutomaticSearchEnabled(It.IsAny<bool>()))
                  .Returns(new List<IIndexer> { indexer.Object });

            Mocker.GetMock<IMakeMangaDownloadDecision>()
                  .Setup(d => d.GetSearchDecision(It.IsAny<List<ReleaseInfo>>(), It.IsAny<MangaSearchCriteriaBase>()))
                  .Returns(_decisions);
        }

        private MangaSearchCriteria BuildMangaCriteria()
        {
            return new MangaSearchCriteria
            {
                Manga = _manga,
                Chapters = new List<Core.Manga.Chapter>(),
                UserInvokedSearch = true,
                InteractiveSearch = true,
                MonitoredChaptersOnly = false
            };
        }

        private ChapterSearchCriteria BuildChapterCriteria()
        {
            return new ChapterSearchCriteria
            {
                Manga = _manga,
                Chapters = new List<Core.Manga.Chapter>(),
                UserInvokedSearch = true,
                InteractiveSearch = true,
                MonitoredChaptersOnly = false
            };
        }

        // RECON-02 / D-01: synthesis fires once per MangaSearch, after the decision call.
        [Test]
        public async Task MangaSearch_should_invoke_SynthesizeFromDecisions_once()
        {
            await Subject.MangaSearch(BuildMangaCriteria());

            Mocker.GetMock<IChapterSynthesisService>()
                  .Verify(s => s.SynthesizeFromDecisions(_manga, _decisions), Times.Once);
        }

        // Synthesis is a side-effect: MangaSearch returns the decision-maker output unchanged.
        [Test]
        public async Task MangaSearch_should_return_decisions_unchanged()
        {
            var result = await Subject.MangaSearch(BuildMangaCriteria());

            result.Should().BeEquivalentTo(_decisions);
            result.Should().HaveCount(_decisions.Count);
        }

        // RESEARCH Open Question #1: ChapterSearch does NOT synthesize (the chapter-scoped
        // path relies on the on-grab hook in MangaReleaseController).
        [Test]
        public async Task ChapterSearch_should_not_invoke_SynthesizeFromDecisions()
        {
            await Subject.ChapterSearch(BuildChapterCriteria());

            Mocker.GetMock<IChapterSynthesisService>()
                  .Verify(s => s.SynthesizeFromDecisions(It.IsAny<Core.Manga.Manga>(), It.IsAny<List<MangaDownloadDecision>>()), Times.Never);
        }

        // ---------------- debug `per-manga-search-2-runs` (2026-06-21): same-tick re-decision ----------------
        // When synthesis writes new Chapter rows, the search re-runs the decision pass against the
        // now-updated catalog so the freshly-synthesized chapters grab in THIS search instead of
        // requiring a second per-manga search. Mirrors MangaRssSyncService's same-tick re-grab.

        // Re-decision fires once: two GetSearchDecision passes, synthesis runs exactly once, and the
        // SECOND (post-synthesis) decision list is what is returned to the grab pipeline.
        [Test]
        public async Task MangaSearch_should_re_decide_against_updated_catalog_when_synthesis_created_rows()
        {
            var firstPass = new List<MangaDownloadDecision>
            {
                new MangaDownloadDecision(new RemoteChapter { Release = new ReleaseInfo { Title = "Chapter 1" } })
            };
            var secondPass = new List<MangaDownloadDecision>
            {
                new MangaDownloadDecision(new RemoteChapter { Release = new ReleaseInfo { Title = "Chapter 1" } }),
                new MangaDownloadDecision(new RemoteChapter { Release = new ReleaseInfo { Title = "Chapter 2" } })
            };

            Mocker.GetMock<IMakeMangaDownloadDecision>()
                  .SetupSequence(d => d.GetSearchDecision(It.IsAny<List<ReleaseInfo>>(), It.IsAny<MangaSearchCriteriaBase>()))
                  .Returns(firstPass)
                  .Returns(secondPass);

            Mocker.GetMock<IChapterSynthesisService>()
                  .Setup(s => s.SynthesizeFromDecisions(_manga, It.IsAny<List<MangaDownloadDecision>>()))
                  .Returns(1);

            Mocker.GetMock<IChapterService>()
                  .Setup(c => c.GetChaptersByManga(_manga.Id))
                  .Returns(new List<Core.Manga.Chapter>
                  {
                      new Core.Manga.Chapter { Id = 2, MangaId = _manga.Id, ChapterNumber = 2m, Monitored = true, ChapterFileId = null }
                  });

            var result = await Subject.MangaSearch(BuildMangaCriteria());

            Mocker.GetMock<IMakeMangaDownloadDecision>()
                  .Verify(d => d.GetSearchDecision(It.IsAny<List<ReleaseInfo>>(), It.IsAny<MangaSearchCriteriaBase>()), Times.Exactly(2));
            Mocker.GetMock<IChapterSynthesisService>()
                  .Verify(s => s.SynthesizeFromDecisions(_manga, It.IsAny<List<MangaDownloadDecision>>()), Times.Once);

            result.Should().BeEquivalentTo(secondPass);
        }

        // Steady state (synthesis wrote nothing) — exactly one decision pass; the chapter catalog
        // is never re-read. Guards against re-deciding on every search.
        [Test]
        public async Task MangaSearch_should_NOT_re_decide_when_synthesis_created_nothing()
        {
            // SynthesizeFromDecisions is not set up -> Moq returns default(int) == 0.
            await Subject.MangaSearch(BuildMangaCriteria());

            Mocker.GetMock<IMakeMangaDownloadDecision>()
                  .Verify(d => d.GetSearchDecision(It.IsAny<List<ReleaseInfo>>(), It.IsAny<MangaSearchCriteriaBase>()), Times.Once);
            Mocker.GetMock<IChapterService>()
                  .Verify(c => c.GetChaptersByManga(It.IsAny<int>()), Times.Never);
        }

        // Automatic-search refresh predicate: with MonitoredChaptersOnly=true only the monitored,
        // file-less rows enter the refreshed requested set (mirrors what MangaSearchService.Execute
        // and a manual 2nd search compute). Already-filed + unmonitored rows are excluded so
        // ChapterRequestedSpecification does not widen onto chapters that must not re-grab.
        [Test]
        public async Task MangaSearch_should_refresh_requested_chapters_to_monitored_fileless_for_automatic_search()
        {
            var criteria = new MangaSearchCriteria
            {
                Manga = _manga,
                Chapters = new List<Core.Manga.Chapter>(),
                MonitoredChaptersOnly = true,
                UserInvokedSearch = false,
                InteractiveSearch = false
            };

            var synthesized = new Core.Manga.Chapter { Id = 55, MangaId = _manga.Id, ChapterNumber = 3m, Monitored = true, ChapterFileId = null };
            var alreadyFiled = new Core.Manga.Chapter { Id = 56, MangaId = _manga.Id, ChapterNumber = 1m, Monitored = true, ChapterFileId = 999 };
            var unmonitored = new Core.Manga.Chapter { Id = 57, MangaId = _manga.Id, ChapterNumber = 2m, Monitored = false, ChapterFileId = null };

            Mocker.GetMock<IChapterSynthesisService>()
                  .Setup(s => s.SynthesizeFromDecisions(_manga, It.IsAny<List<MangaDownloadDecision>>()))
                  .Returns(1);
            Mocker.GetMock<IChapterService>()
                  .Setup(c => c.GetChaptersByManga(_manga.Id))
                  .Returns(new List<Core.Manga.Chapter> { synthesized, alreadyFiled, unmonitored });

            await Subject.MangaSearch(criteria);

            criteria.Chapters.Should().ContainSingle();
            criteria.Chapters.Should().OnlyContain(c => c.Id == 55);
        }
    }
}
