using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Manga;
using NzbDrone.Core.DecisionEngine.Manga.Specifications;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.CustomFormats;
using NzbDrone.Core.Profiles.Translations;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests.Manga
{
    // F-01-class regression mitigation per 05-VALIDATION.md Wave 0 Requirements + 05-RESEARCH.md Pitfall 5.
    //
    // The risk this fixture mitigates: spec classes are registered in DI but never actually CALLED
    // by the orchestrator (Phase 3 F-01 was exactly this — MangaDexIndexer injected
    // IIndexerSourceStatusService but never invoked it; caught by sonarr-consistency-audit, NOT by tests).
    //
    // This fixture instantiates the REAL MangaDownloadDecisionMaker + REAL spec set + REAL comparer
    // (only external services like ITranslationProfileService get mocked). Each test asserts that
    // the actual rejection / acceptance / ranking happens end-to-end — proving the orchestrator
    // calls the specs AND that the comparer applies D-08 ordering.
    //
    // NOTE: MangaParser.ParseChapterTitle is a static pure-function call (no DI). Tests use realistic
    // chapter titles that the parser can extract. The IMangaParsingService.Map / .GetManga calls
    // ARE mockable — those are stubbed to short-circuit DB resolution. The CF augmentation site
    // (ICustomFormatCalculationService.ParseCustomFormat) is also mockable; we drive
    // RemoteChapter.CustomFormatScore via the (mocked) CustomFormatProfile.CalculateCustomFormatScore
    // path the maker computes inline.
    [TestFixture]
    public class MangaDownloadDecisionMakerEndToEndFixture : CoreTest
    {
        private MangaDownloadDecisionMaker _maker;
        private MangaDownloadDecisionComparer _comparer;
        private List<IMangaDecisionEngineSpecification> _specs;

        private Mock<ITranslationProfileService> _translationProfileService;
        private Mock<ICustomFormatProfileService> _customFormatProfileService;
        private Mock<IConfigService> _configService;
        private Mock<IMangaParsingService> _parsingService;
        private Mock<ICustomFormatCalculationService> _formatCalculator;
        private Mock<IIndexerFactory> _indexerFactory;

        [SetUp]
        public void Setup()
        {
            _translationProfileService = new Mock<ITranslationProfileService>();
            _customFormatProfileService = new Mock<ICustomFormatProfileService>();
            _configService = new Mock<IConfigService>();
            _parsingService = new Mock<IMangaParsingService>();
            _formatCalculator = new Mock<ICustomFormatCalculationService>();
            _indexerFactory = new Mock<IIndexerFactory>();    // BL-02 — canonical SourceKey resolver

            // REAL spec instances (no Mocker — exercise the actual code paths). These are the two
            // gates the F-01 fixture proves fire end-to-end. Other operational specs (MonitoredManga,
            // MonitoredChapter, etc.) are exercised by their own per-spec fixtures; what matters here
            // is that TPROFILE outer + CF inner gates ACTUALLY EVALUATE on real components.
            _specs = new List<IMangaDecisionEngineSpecification>
            {
                new LanguageInTranslationProfileSpecification(_translationProfileService.Object, _configService.Object, LogManager.GetLogger("test")),
                new CustomFormatMinimumScoreSpecification(_customFormatProfileService.Object, _configService.Object, LogManager.GetLogger("test"))
            };

            _maker = new MangaDownloadDecisionMaker(
                _specs,
                _parsingService.Object,
                _formatCalculator.Object,
                _customFormatProfileService.Object,
                _indexerFactory.Object,
                _configService.Object,
                LogManager.GetLogger("test"));

            _comparer = new MangaDownloadDecisionComparer(_translationProfileService.Object, _configService.Object);
        }

        [Test]
        public void TPROFILE_outer_gate_actually_rejects_release_when_language_not_in_profile()
        {
            // Profile = ["en"] strict mode
            _translationProfileService.Setup(s => s.Get(7))
                .Returns(new TranslationProfile { Id = 7, Name = "EN-only", Languages = new List<string> { "en" }, AllowLanguagesNotInProfile = false });

            var manga = new NzbDrone.Core.Manga.Manga { Id = 1, Title = "Test Manga", Monitored = true, TranslationProfileId = 7 };

            // The static MangaParser will extract MangaTitle from this realistic title; the parsing
            // service is mocked to return our fixture Manga regardless of input. Korean (ko) release
            // is expected to be REJECTED by the LanguageInTranslationProfileSpecification.
            var release = new ReleaseInfo { Title = "Test Manga - Chapter 042 [Bad Group]", TranslatedLanguage = "ko" };
            var remoteChapter = new RemoteChapter
            {
                Manga = manga,
                Chapters = new List<Chapter> { new() { Id = 100, Monitored = true, ChapterNumber = 42m } },
                ParsedChapterInfo = new ParsedChapterInfo()
            };

            _parsingService.Setup(p => p.GetManga(It.IsAny<string>())).Returns(manga);
            _parsingService.Setup(p => p.Map(It.IsAny<ParsedChapterInfo>(), It.IsAny<NzbDrone.Core.Manga.Manga>(), It.IsAny<IList<Chapter>>())).Returns(remoteChapter);
            _formatCalculator.Setup(f => f.ParseCustomFormat(It.IsAny<MangaCustomFormatInput>())).Returns(new List<CustomFormat>());

            var decisions = _maker.GetRssDecision(new List<ReleaseInfo> { release });

            // F-01-class assertion: the spec was actually CALLED — not just injected.
            decisions.Should().HaveCount(1);
            decisions[0].Approved.Should().BeFalse("LanguageInTranslationProfileSpecification must reject Korean when profile=['en'] strict");
            decisions[0].Rejections.Should().Contain(r => r.Reason == DownloadRejectionReason.LanguageNotInProfile);
        }

        [Test]
        public void CF_inner_score_gate_actually_rejects_release_when_score_below_minimum()
        {
            _translationProfileService.Setup(s => s.Get(7))
                .Returns(new TranslationProfile { Id = 7, Languages = new List<string> { "en" }, AllowLanguagesNotInProfile = false });

            // CustomFormatProfile with MinFormatScore=0 + a CalculateCustomFormatScore that yields -100.
            // We use a real CustomFormatProfile object with FormatItems that score -100 against the
            // CF returned by the mocked calculator (so the maker computes score=-100 itself via the
            // CF augmentation site at MangaDownloadDecisionMaker.cs:131-135).
            var badCf = new CustomFormat { Id = 99, Name = "Bad" };
            var profile = new CustomFormatProfile
            {
                Id = 11,
                Name = "WithGate",
                MinFormatScore = 0,
                MaxFormatScore = null,
                FormatItems = new List<ProfileFormatItem>
                {
                    new() { Format = badCf, Score = -100 }
                }
            };
            _customFormatProfileService.Setup(s => s.Get(11)).Returns(profile);

            var manga = new NzbDrone.Core.Manga.Manga { Id = 1, Title = "Test", Monitored = true, TranslationProfileId = 7, CustomFormatProfileId = 11 };
            var release = new ReleaseInfo { Title = "Test - Chapter 042 [bad]", TranslatedLanguage = "en" };
            var remoteChapter = new RemoteChapter
            {
                Manga = manga,
                Chapters = new List<Chapter> { new() { Id = 100, Monitored = true, ChapterNumber = 42m } },
                ParsedChapterInfo = new ParsedChapterInfo()
            };

            _parsingService.Setup(p => p.GetManga(It.IsAny<string>())).Returns(manga);
            _parsingService.Setup(p => p.Map(It.IsAny<ParsedChapterInfo>(), It.IsAny<NzbDrone.Core.Manga.Manga>(), It.IsAny<IList<Chapter>>())).Returns(remoteChapter);

            // CF augmentation site: return the bad CF so the maker computes score=-100 via
            // profile.CalculateCustomFormatScore(...).
            _formatCalculator.Setup(f => f.ParseCustomFormat(It.IsAny<MangaCustomFormatInput>()))
                .Returns(new List<CustomFormat> { badCf });

            var decisions = _maker.GetRssDecision(new List<ReleaseInfo> { release });

            // F-01-class assertion: CF augmentation site fires AND CustomFormatMinimumScoreSpecification reads it.
            decisions.Should().HaveCount(1);
            decisions[0].Approved.Should().BeFalse("CustomFormatMinimumScoreSpecification must reject when score < MinFormatScore");
            decisions[0].Rejections.Should().Contain(r => r.Reason == DownloadRejectionReason.CustomFormatMinimumScore);
        }

        [Test]
        public void Comparer_ranks_language_outer_above_CF_score_inner()
        {
            // D-08 ordering: language rank (en=0) outranks CF score (1000).
            // Comparer is consumed via OrderByDescending — Compare(a, b) > 0 means `a` should sort
            // FIRST (winner). See MangaDownloadDecisionComparerFixture for the semantics note.
            _translationProfileService.Setup(s => s.Get(7))
                .Returns(new TranslationProfile { Id = 7, Languages = new List<string> { "en", "es" }, AllowLanguagesNotInProfile = false });

            var manga = new NzbDrone.Core.Manga.Manga { Id = 1, TranslationProfileId = 7 };
            var aRc = new RemoteChapter { Manga = manga, Release = new ReleaseInfo { TranslatedLanguage = "en", IndexerPriority = 50 }, CustomFormatScore = 10 };
            var bRc = new RemoteChapter { Manga = manga, Release = new ReleaseInfo { TranslatedLanguage = "es", IndexerPriority = 50 }, CustomFormatScore = 1000 };
            var aDec = new MangaDownloadDecision(aRc);
            var bDec = new MangaDownloadDecision(bRc);

            // a (en, low CF) wins over b (es, high CF) because language is the outer gate per D-08.
            _comparer.Compare(aDec, bDec).Should().BeGreaterThan(0, "F-01: language rank outranks CF score regardless of magnitude");
            _comparer.Compare(bDec, aDec).Should().BeLessThan(0);
        }

        [Test]
        public void Indexer_priority_promoted_above_age_and_size()
        {
            _translationProfileService.Setup(s => s.Get(7))
                .Returns(new TranslationProfile { Id = 7, Languages = new List<string> { "en" } });

            var manga = new NzbDrone.Core.Manga.Manga { Id = 1, TranslationProfileId = 7 };

            // Same language + CF score; A is fresher (PublishDate = now-1h) but B has lower (better)
            // IndexerPriority. B should win — IndexerPriority promoted above Age + Size per D-08.
            var aRc = new RemoteChapter
            {
                Manga = manga,
                Release = new ReleaseInfo
                {
                    TranslatedLanguage = "en",
                    IndexerPriority = 10,
                    PublishDate = DateTime.UtcNow.AddHours(-1),
                    Size = 1000
                },
                CustomFormatScore = 50
            };
            var bRc = new RemoteChapter
            {
                Manga = manga,
                Release = new ReleaseInfo
                {
                    TranslatedLanguage = "en",
                    IndexerPriority = 5,
                    PublishDate = DateTime.UtcNow.AddHours(-720),
                    Size = 999
                },
                CustomFormatScore = 50
            };
            var aDec = new MangaDownloadDecision(aRc);
            var bDec = new MangaDownloadDecision(bRc);

            // b (lower IndexerPriority, much older) wins over a (higher IndexerPriority, fresh).
            // Compare(b, a) > 0 means b sorts first under OrderByDescending.
            _comparer.Compare(bDec, aDec).Should().BeGreaterThan(0, "D-08: IndexerPriority promoted above Age (b is older) and Size (a is bigger)");
            _comparer.Compare(aDec, bDec).Should().BeLessThan(0);
        }

        [Test]
        public void All_eleven_manga_specs_auto_discovered_via_assembly_reflection()
        {
            // F-01 + Pitfall 6 mitigation per 05-VALIDATION.md Wave 0: assert the FULL spec set
            // implements IMangaDecisionEngineSpecification and is reachable from the production
            // assembly. DryIoc IEnumerable<IMangaDecisionEngineSpecification> in production resolves
            // to exactly this set via reflection-based assembly scanning (pattern S1).
            //
            // The 11 specs (per D-06) are: MonitoredManga, MonitoredChapter, ChapterRequested,
            // AlreadyImportedChapter, Blocklist (Phase 5 stub), LanguageInTranslationProfile,
            // CustomFormatMinimumScore, MinimumAge, AcceptableSize, MaximumSize, QueueDuplicate.
            //
            // We use reflection on the loaded NzbDrone.Core assembly because the AutoMoqer test
            // container does NOT auto-discover concrete spec types via DryIoc (it falls back to
            // mocks for interfaces). Reflection over the production assembly gives us the same
            // set production DI sees at runtime.
            var coreAssembly = typeof(IMangaDecisionEngineSpecification).Assembly;
            var specTypes = coreAssembly
                .GetTypes()
                .Where(t => !t.IsInterface
                            && !t.IsAbstract
                            && typeof(IMangaDecisionEngineSpecification).IsAssignableFrom(t))
                .ToList();

            specTypes.Count.Should().Be(11,
                "exactly 11 manga decision-engine specs ship per D-06; any extra suggests a TV spec was "
                + "accidentally cross-tagged via IMangaDecisionEngineSpecification (Pitfall 6 — a class "
                + "that implements both IMangaDecisionEngineSpecification AND IDownloadDecisionEngineSpecification "
                + "would auto-discover into both makers and NRE on the wrong subject type at runtime). "
                + "Missing specs fail to fire at runtime.");

            // WR-05: defensive cross-check — no spec must implement BOTH the manga and TV
            // decision-engine interfaces simultaneously (Pitfall 6).
            var crossTagged = specTypes
                .Where(t => typeof(IDownloadDecisionEngineSpecification).IsAssignableFrom(t))
                .ToList();
            crossTagged.Should().BeEmpty(
                "Pitfall 6: a spec must implement only one decision-engine interface — cross-tagged "
                + "specs auto-discover into both makers and NRE on the wrong subject type at runtime.");
        }
    }
}
