using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine.Manga;
using NzbDrone.Core.DecisionEngine.Manga.Specifications;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.CustomFormats;
using NzbDrone.Core.Profiles.Translations;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerSearchTests.Manga
{
    // DEF-19-02-01 regression fixture — InteractiveSearch rendered 0 rows despite the
    // indexer returning releases. Drives the REAL MangaDownloadDecisionMaker + REAL
    // MangaParsingService against a DB-seeded Komi manga and the exact ReleaseInfo shape
    // MangaDexParser produces from the recorded indexer-feed cassette
    // (5986f816c5068c78.json: Komi chapters 288/500/500.5).
    //
    // Two-part bug this pins:
    //   1. MangaDexParser builds ReleaseInfo.Title from MangaDex attributes.title (the
    //      ja-ro romanization "Komi-san wa Komyushou Desu." for Komi — no "en" key) while
    //      MangaDexMetadataSource stores Manga.Title from the English altTitles ("Komi
    //      Can't Communicate"). The normalized forms do not match, so the original failure
    //      shape was: `_parsingService.GetManga(parsedChapterInfo.MangaTitle)` returned
    //      null and EVERY release became an UnknownSeries-rejected decision with
    //      RemoteChapter.Manga == null.
    //
    //      Initial fix (commit 77a114221): short-circuit GetManga with
    //      `searchCriteria.Manga ?? GetManga(...)` on the search path. This regressed in
    //      gh118 — every search-path release was force-attributed to the searched manga,
    //      including cross-title indexer noise.
    //
    //      gh118 fix (this fixture's current pinned behavior): Sonarr-canonical multi-
    //      strategy MangaParsingService.GetManga that consults a persisted
    //      AlternativeTitles set populated by metadata sources from each provider's
    //      alt-title field set. The romanized attributes.title that SelectPreferredTitle
    //      discards as the canonical Manga.Title is captured in AlternativeTitles, so
    //      Strategy 2 (FindByAlternativeTitle) resolves the romanized indexer-feed title
    //      back to the English-stored Manga. The force-assign is reverted; the existing
    //      MangaSpecification is restored to its designed Id-cross-check role.
    //
    //      This fixture's seeding pre-populates AlternativeTitles with the normalized
    //      romanization to simulate what MangaDexMetadataSource.MapManga now writes.
    //   2. A Manga can carry CustomFormatProfileId == 0 (the int default the AddManga
    //      modal sends when no CF profile is chosen). The maker's CF-score step called
    //      ICustomFormatProfileService.Get(0), which throws ModelNotFoundException — caught
    //      by the maker's outer try/catch, turning every release into a DecisionError
    //      rejection. The fix degrades a zero / orphaned profile id to score 0.
    //
    // Built directly (not via Mocker.Resolve of the full auto-discovered spec set) with a
    // controlled spec list + mocked services — the component under test is the maker's
    // resolution + CF-score path and the REAL MangaParsingService title match; the
    // operational specs have their own per-spec fixtures.
    [TestFixture]
    public class InteractiveSearchZeroRowReproFixture : DbTest
    {
        private NzbDrone.Core.Manga.Manga _manga;
        private List<NzbDrone.Core.Manga.Chapter> _chapters;
        private MangaDownloadDecisionMaker _maker;
        private Mock<ICustomFormatCalculationService> _formatCalculator;
        private Mock<ICustomFormatProfileService> _customFormatProfileService;
        private Mock<ITranslationProfileService> _translationProfileService;
        private Mock<IConfigService> _configService;
        private Mock<IIndexerFactory> _indexerFactory;

        [SetUp]
        public void Setup()
        {
            // DB-seed the manga exactly as AddManga would after a MangaDex metadata sync:
            // Title is the altTitles-preferred English title; CleanTitle is its normalized form.
            var mangaRepo = Mocker.Resolve<MangaRepository>();
            _manga = mangaRepo.Insert(new NzbDrone.Core.Manga.Manga
            {
                Title = "Komi Can't Communicate",
                CleanTitle = MangaTitleNormalizer.Normalize("Komi Can't Communicate"),
                TitleSlug = "komi-cant-communicate",
                Path = "/manga/Komi Cant Communicate",
                Monitored = true,

                // The real-world trigger for the second half of the bug: the AddManga modal
                // sends 0 when no CF profile is chosen and none is the global default.
                CustomFormatProfileId = 0,

                // gh118 — AlternativeTitles is now populated by MangaDexMetadataSource.MapManga
                // from attributes.title + attributes.altTitles. The seeded value mirrors what
                // CollectAlternativeTitles would write in production: the ja-ro romanization
                // that SelectPreferredTitle discarded when choosing the English Manga.Title.
                // MangaParsingService.GetManga Strategy 2 (FindByAlternativeTitle) reads this
                // to resolve the romanized indexer-feed title back to the English-stored manga.
                AlternativeTitles = new List<string>
                {
                    MangaTitleNormalizer.Normalize("Komi-san wa Komyushou Desu."),
                },
            });

            // DB-seed the chapters the AddManga metadata feed would have synced. The recorded
            // indexer-feed cassette returns chapters 288, 500, 500.5.
            var chapterRepo = Mocker.Resolve<ChapterRepository>();
            _chapters = new List<NzbDrone.Core.Manga.Chapter>();
            foreach (var num in new[] { 288m, 500m, 500.5m })
            {
                _chapters.Add(chapterRepo.Insert(new NzbDrone.Core.Manga.Chapter
                {
                    MangaId = _manga.Id,
                    ChapterNumber = num,
                    Monitored = true,
                }));
            }

            _formatCalculator = new Mock<ICustomFormatCalculationService>();
            _formatCalculator.Setup(f => f.ParseCustomFormat(It.IsAny<MangaCustomFormatInput>()))
                             .Returns(new List<CustomFormat>());

            _customFormatProfileService = new Mock<ICustomFormatProfileService>();

            // Faithful reproduction of the production failure mode: Get(0) throws
            // ModelNotFoundException (BasicRepository.Get on a missing row). Pre-fix this
            // turned every release into a DecisionError rejection.
            _customFormatProfileService.Setup(s => s.Get(It.IsAny<int>()))
                .Throws(new NzbDrone.Core.Datastore.ModelNotFoundException(typeof(CustomFormatProfile), 0));

            _translationProfileService = new Mock<ITranslationProfileService>();
            _configService = new Mock<IConfigService>();
            _indexerFactory = new Mock<IIndexerFactory>();

            // REAL MangaParsingService — the component under test for the title-resolution
            // half of the bug. Resolved from the container so it gets the real
            // IMangaService / IChapterService / repos against the seeded DB.
            //
            // gh118: explicitly register real repos + services so the 3-strategy
            // GetManga (FindByTitle / FindByAlternativeTitle / FindByTitleInexact)
            // actually queries the DB. Default AutoMoq would inject mocked
            // IMangaRepository / IMangaService that return null for everything,
            // so GetManga would resolve null even with the alt-title populated on
            // the seeded manga. Cascade order: repo → service.
            Mocker.SetConstant<IMangaRepository>(Mocker.Resolve<MangaRepository>());
            Mocker.SetConstant<IChapterRepository>(Mocker.Resolve<ChapterRepository>());
            Mocker.SetConstant<IMangaService>(Mocker.Resolve<MangaService>());
            Mocker.SetConstant<IChapterService>(Mocker.Resolve<ChapterService>());
            var parsingService = Mocker.Resolve<MangaParsingService>();

            // Controlled spec list: the LanguageInTranslationProfile + CustomFormatMinimumScore
            // gates are exactly the two profile-id-driven specs this bug routed through. Other
            // operational specs have their own fixtures and are not part of this regression.
            var specs = new List<IMangaDecisionEngineSpecification>
            {
                new LanguageInTranslationProfileSpecification(
                    _translationProfileService.Object, _configService.Object, LogManager.GetLogger("test")),
                new CustomFormatMinimumScoreSpecification(
                    _customFormatProfileService.Object, _configService.Object, LogManager.GetLogger("test")),
            };

            _maker = new MangaDownloadDecisionMaker(
                specs,
                parsingService,
                _formatCalculator.Object,
                _customFormatProfileService.Object,
                _indexerFactory.Object,
                _configService.Object,
                LogManager.GetLogger("test"));
        }

        private static List<ReleaseInfo> CassetteReleases()
        {
            // Exact ReleaseInfo shape MangaDexParser.ParseResponse produces from the recorded
            // indexer-feed cassette (5986f816c5068c78.json). mangaTitle = the ja-ro
            // romanization because attributes.title has no "en" key.
            ReleaseInfo Make(decimal chapter) => new ReleaseInfo
            {
                Guid = $"mangadex-test-{chapter}",
                Title = $"Komi-san wa Komyushou Desu. - Chapter {chapter.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} [en] [komi-scan wa komyushou desu]",
                DownloadUrl = $"https://api.mangadex.org/at-home/server/{chapter}",
                DownloadProtocol = DownloadProtocol.Http,
                ScanlationGroup = "komi-scan wa komyushou desu",
                TranslatedLanguage = "en",
            };

            return new List<ReleaseInfo> { Make(288m), Make(500m), Make(500.5m) };
        }

        [Test]
        public void Search_resolves_indexer_feed_releases_onto_the_searched_manga()
        {
            var criteria = new MangaSearchCriteria
            {
                Manga = _manga,
                Chapters = _chapters,
                UserInvokedSearch = true,
                InteractiveSearch = true,
                MonitoredChaptersOnly = false,
            };

            var decisions = _maker.GetSearchDecision(CassetteReleases(), criteria);

            // The decision maker must emit one decision per release (the modal renders rejected
            // rows too). A zero count would be the DEF-19-02-01 backend symptom.
            decisions.Should().HaveCount(3, "the indexer returned 3 releases and the modal renders rejected rows too");

            // ROOT CAUSE #1: RemoteChapter must resolve onto the DB-seeded manga. Pre-fix the
            // parser-extracted romanized title failed to normalize-match Manga.CleanTitle and
            // every release fell through to UnknownManga with RemoteChapter.Manga == null.
            decisions.Should().OnlyContain(
                d => d.RemoteChapter != null && d.RemoteChapter.Manga != null,
                "every release must resolve onto the searched manga, not fall through to UnknownManga");

            // ROOT CAUSE #2: a CustomFormatProfileId of 0 must NOT throw the release into a
            // DecisionError rejection. With the searched manga resolved and the profile gates
            // degrading gracefully on a zero id, the releases are Approved (no rejections).
            decisions.Should().OnlyContain(
                d => d.Approved,
                "a zero / orphaned CustomFormatProfile id must degrade gracefully, not DecisionError-reject the release");
        }
    }
}
