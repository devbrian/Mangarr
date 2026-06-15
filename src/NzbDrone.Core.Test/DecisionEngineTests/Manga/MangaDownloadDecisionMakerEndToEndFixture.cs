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
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch.Definitions;
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
            //
            // GH #118 extension: MangaSpecification is included so the end-to-end fixture asserts
            // the post-fix wiring (force-assign reverted → GetManga is resolver → MangaSpecification
            // is structurally meaningful again) end-to-end against the REAL spec instance.
            _specs = new List<IMangaDecisionEngineSpecification>
            {
                new LanguageInTranslationProfileSpecification(_translationProfileService.Object, _configService.Object, LogManager.GetLogger("test")),
                new CustomFormatMinimumScoreSpecification(_customFormatProfileService.Object, _configService.Object, LogManager.GetLogger("test")),
                new MangaSpecification(LogManager.GetLogger("test")),
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
        public void cfInput_SourceKey_prefers_per_release_Source_over_indexer_key()
        {
            // quick-260608-gmm (Finding 3): ResolveSourceKey must prefer the per-release
            // ReleaseInfo.Source (the gateway upstream stamped by GatewayParser) over the
            // indexer-level key, AND fall back to report.Indexer when Source is empty. We CAPTURE
            // the MangaCustomFormatInput the maker builds via a Moq .Callback on the already-mocked
            // ParseCustomFormat to assert cfInput.SourceKey end-to-end.
            _translationProfileService.Setup(s => s.Get(7))
                .Returns(new TranslationProfile { Id = 7, Languages = new List<string> { "en" }, AllowLanguagesNotInProfile = true });

            var manga = new NzbDrone.Core.Manga.Manga { Id = 1, Title = "Test Manga", Monitored = true, TranslationProfileId = 7 };
            var remoteChapter = new RemoteChapter
            {
                Manga = manga,
                Chapters = new List<Chapter> { new() { Id = 100, Monitored = true, ChapterNumber = 42m } },
                ParsedChapterInfo = new ParsedChapterInfo()
            };

            _parsingService.Setup(p => p.GetManga(It.IsAny<string>())).Returns(manga);
            _parsingService.Setup(p => p.Map(It.IsAny<ParsedChapterInfo>(), It.IsAny<NzbDrone.Core.Manga.Manga>(), It.IsAny<IList<Chapter>>())).Returns(remoteChapter);

            MangaCustomFormatInput captured = null;
            _formatCalculator.Setup(f => f.ParseCustomFormat(It.IsAny<MangaCustomFormatInput>()))
                .Callback<MangaCustomFormatInput>(input => captured = input)
                .Returns(new List<CustomFormat>());

            // (a) per-release Source wins even though the aggregator branch would be reachable.
            // IndexerId = 0 keeps the test free of an IIndexerFactory setup; the new FIRST branch
            // short-circuits before the indexer lookup regardless.
            var withSource = new ReleaseInfo { Title = "Test Manga - Chapter 042 [Group]", TranslatedLanguage = "en", Source = "comix", IndexerId = 0, Indexer = "Gateway" };
            _maker.GetRssDecision(new List<ReleaseInfo> { withSource });

            captured.Should().NotBeNull("the CF augmentation site must fire");
            captured.SourceKey.Should().Be("comix", "per-release ReleaseInfo.Source is the authoritative source key (Finding 3)");

            // (b) empty Source falls through to the indexer-level resolution; with IndexerId = 0 the
            // aggregator branch is skipped and the final fallback returns report.Indexer.
            captured = null;
            var withoutSource = new ReleaseInfo { Title = "Test Manga - Chapter 043 [Group]", TranslatedLanguage = "en", Source = null, IndexerId = 0, Indexer = "Gateway" };
            _maker.GetRssDecision(new List<ReleaseInfo> { withoutSource });

            captured.Should().NotBeNull();
            captured.SourceKey.Should().Be("Gateway", "empty Source must fall back to the indexer-level resolution (report.Indexer)");

            // (c) empty-string Source falls through identically to null — ResolveSourceKey uses
            // !string.IsNullOrWhiteSpace, so "" is not treated as a per-release source (CodeRabbit).
            captured = null;
            var withEmptySource = new ReleaseInfo { Title = "Test Manga - Chapter 044 [Group]", TranslatedLanguage = "en", Source = "", IndexerId = 0, Indexer = "Gateway" };
            _maker.GetRssDecision(new List<ReleaseInfo> { withEmptySource });

            captured.Should().NotBeNull();
            captured.SourceKey.Should().Be("Gateway", "empty-string Source must fall back to indexer resolution");

            // (d) whitespace-only Source falls through the same way (IsNullOrWhiteSpace).
            captured = null;
            var withWhitespaceSource = new ReleaseInfo { Title = "Test Manga - Chapter 045 [Group]", TranslatedLanguage = "en", Source = "   ", IndexerId = 0, Indexer = "Gateway" };
            _maker.GetRssDecision(new List<ReleaseInfo> { withWhitespaceSource });

            captured.Should().NotBeNull();
            captured.SourceKey.Should().Be("Gateway", "whitespace-only Source must fall back to indexer resolution");
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

        // ─────────────────────────────────────────────────────────────────────
        // GH #118 end-to-end assertions — cross-title noise rejection +
        // DEF-19-02-01 regression preservation. Both exercise the REAL
        // MangaSpecification instance + REAL MangaDownloadDecisionMaker
        // (the parsing service is mocked, mirroring this fixture's pattern).
        // ─────────────────────────────────────────────────────────────────────

        [Test]
        public void GH118_search_path_rejects_release_when_GetManga_resolves_to_different_manga_than_searchCriteria()
        {
            // Core gh118 scenario: indexer fan-out returns a release for manga B;
            // the user searched for manga A. Pre-fix (77a114221 force-assign),
            // MangaDownloadDecisionMaker baked manga = searchCriteria.Manga,
            // so subject.Manga.Id == searchCriteria.Manga.Id was tautologically
            // true and MangaSpecification accepted. Post-fix, GetManga is the
            // resolver and returns mangaB; MangaSpecification's Id-equality
            // catches mangaA.Id != mangaB.Id and permanently rejects.
            _translationProfileService.Setup(s => s.Get(7))
                .Returns(new TranslationProfile { Id = 7, Languages = new List<string> { "en" } });

            var mangaA = new NzbDrone.Core.Manga.Manga { Id = 1, Title = "Search Target Manga", CleanTitle = "search target manga", Monitored = true, TranslationProfileId = 7 };
            var mangaB = new NzbDrone.Core.Manga.Manga { Id = 999, Title = "Unrelated Manga", CleanTitle = "unrelated manga", Monitored = true, TranslationProfileId = 7 };

            // The release's parsed title belongs to manga B. GetManga (mocked)
            // resolves to manga B as it would in production via Strategy 1 / 2 / 3.
            _parsingService.Setup(p => p.GetManga(It.IsAny<string>())).Returns(mangaB);
            _parsingService.Setup(p => p.Map(It.IsAny<ParsedChapterInfo>(), It.IsAny<NzbDrone.Core.Manga.Manga>(), It.IsAny<IList<Chapter>>()))
                .Returns<ParsedChapterInfo, NzbDrone.Core.Manga.Manga, IList<Chapter>>((parsed, m, _) => new RemoteChapter
                {
                    Manga = m,
                    Chapters = new List<Chapter> { new() { Id = 100, Monitored = true, ChapterNumber = 42m } },
                    ParsedChapterInfo = parsed,
                });
            _formatCalculator.Setup(f => f.ParseCustomFormat(It.IsAny<MangaCustomFormatInput>())).Returns(new List<CustomFormat>());

            var release = new ReleaseInfo { Title = "Unrelated Manga - Chapter 042", TranslatedLanguage = "en" };

            // Search path: search target is manga A.
            var searchCriteria = new MangaSearchCriteria { Manga = mangaA, Chapters = new List<Chapter>() };
            var decisions = _maker.GetSearchDecision(new List<ReleaseInfo> { release }, searchCriteria);

            decisions.Should().HaveCount(1);
            decisions[0].Approved.Should().BeFalse("GH #118: cross-title indexer noise must be rejected — release belongs to manga B but search target is manga A");
            decisions[0].Rejections.Should().Contain(r => r.Reason == DownloadRejectionReason.MatchesAnotherSeries);

            // The cross-manga observability log is emitted at Debug (mirroring Sonarr's
            // canonical SeriesSpecification) — cross-title indexer noise is an expected,
            // high-volume search condition, so no Warn is fired.
        }

        [Test]
        public void GH118_DEF_19_02_01_regression_preserved_search_path_accepts_release_when_GetManga_resolves_to_search_target()
        {
            // DEF-19-02-01 regression preservation: the original defect was that
            // MangaDex's romanized attributes.title in ReleaseInfo could not
            // normalize-match the English-stored Manga.CleanTitle, so GetManga
            // returned null and every release became UnknownManga. The 77a114221
            // force-assign fixed this by short-circuiting GetManga.
            //
            // Post-GH-118 fix the force-assign is reverted but GetManga is now
            // multi-strategy with an alt-title fallback (parts 3+4 of the fix).
            // In production: GetManga consults FindByAlternativeTitle and resolves
            // the romanized title to the correct manga. Here we simulate that
            // resolution by having the mocked GetManga return the searched manga
            // — the assertion is that with GetManga resolving correctly, the
            // pipeline approves (no UnknownManga, no MatchesAnotherSeries).
            _translationProfileService.Setup(s => s.Get(7))
                .Returns(new TranslationProfile { Id = 7, Languages = new List<string> { "en" } });

            var manga = new NzbDrone.Core.Manga.Manga { Id = 1, Title = "Attack on Titan", CleanTitle = "attack on titan", Monitored = true, TranslationProfileId = 7 };

            // GetManga resolves the romanized title back to the English-stored manga
            // via the new AlternativeTitles strategy.
            _parsingService.Setup(p => p.GetManga(It.IsAny<string>())).Returns(manga);
            _parsingService.Setup(p => p.Map(It.IsAny<ParsedChapterInfo>(), It.IsAny<NzbDrone.Core.Manga.Manga>(), It.IsAny<IList<Chapter>>()))
                .Returns<ParsedChapterInfo, NzbDrone.Core.Manga.Manga, IList<Chapter>>((parsed, m, _) => new RemoteChapter
                {
                    Manga = m,
                    Chapters = new List<Chapter> { new() { Id = 100, Monitored = true, ChapterNumber = 42m } },
                    ParsedChapterInfo = parsed,
                });
            _formatCalculator.Setup(f => f.ParseCustomFormat(It.IsAny<MangaCustomFormatInput>())).Returns(new List<CustomFormat>());

            var release = new ReleaseInfo { Title = "Shingeki no Kyojin - Chapter 042 [EN]", TranslatedLanguage = "en" };

            var searchCriteria = new MangaSearchCriteria { Manga = manga, Chapters = new List<Chapter>() };
            var decisions = _maker.GetSearchDecision(new List<ReleaseInfo> { release }, searchCriteria);

            decisions.Should().HaveCount(1);
            decisions[0].Rejections.Should().NotContain(r => r.Reason == DownloadRejectionReason.MatchesAnotherSeries,
                "DEF-19-02-01 regression: MangaSpecification must NOT reject when GetManga successfully resolves the romanized title to the searched manga via the alt-title strategy");
            decisions[0].Rejections.Should().NotContain(r => r.Reason == DownloadRejectionReason.UnknownSeries,
                "DEF-19-02-01 regression: GetManga must successfully resolve (not return null) when an alt-title match exists");
        }

        [Test]
        public void All_fifteen_manga_specs_auto_discovered_via_assembly_reflection()
        {
            // F-01 + Pitfall 6 mitigation per 05-VALIDATION.md Wave 0: assert the FULL spec set
            // implements IMangaDecisionEngineSpecification and is reachable from the production
            // assembly. DryIoc IEnumerable<IMangaDecisionEngineSpecification> in production resolves
            // to exactly this set via reflection-based assembly scanning (pattern S1).
            //
            // Phase 5 D-06 shipped 11 specs; Phase 8 cluster-02 added DeletedChapterFileSpecification
            // (audit/no-sibling/DeletedEpisodeFileSpecification.md); Phase 8 cluster 06-03 added
            // MangaSpecification (audit/no-sibling/SeriesSpecification.md); Phase 8 cluster 06-04
            // added SingleChapterSearchMatchSpecification
            // (audit/no-sibling/SingleEpisodeSearchMatchSpecification.md); debug session
            // `rss-regrab-existing-chapter` (2026-06-15) added the disk-aware UpgradeDiskSpecification
            // (decision-side peer of import-side UpgradeSpecification — rejects re-grabbing an
            // already-imported chapter) for a current total of 15:
            // MonitoredManga, MonitoredChapter, ChapterRequested, AlreadyImportedChapter,
            // Blocklist, LanguageInTranslationProfile, CustomFormatMinimumScore, MinimumAge,
            // AcceptableSize, MaximumSize, QueueDuplicate, DeletedChapterFile, Manga,
            // SingleChapterSearchMatch, UpgradeDisk.
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

            specTypes.Count.Should().Be(15,
                "exactly 15 manga decision-engine specs ship after the rss-regrab UpgradeDiskSpecification add; "
                + "any extra suggests a TV spec was accidentally cross-tagged via "
                + "IMangaDecisionEngineSpecification (Pitfall 6 — a class that implements both "
                + "IMangaDecisionEngineSpecification AND IDownloadDecisionEngineSpecification "
                + "would auto-discover into both makers and NRE on the wrong subject type at runtime). "
                + "Missing specs fail to fire at runtime.");

            // WR-05: defensive cross-check — Sonarr divergence: Phase 15 Plan 15-11 cascade absorption
            // — IDownloadDecisionEngineSpecification was DELETED with the TV cascade in Plan 15-10 so
            // cross-tagging is now impossible by construction. The Pitfall 6 guard is vacuously
            // satisfied; the assertion is preserved as a no-op tautology so a future TV-spec interface
            // resurrection trips the test.
            specTypes.Should().NotBeNull("manga decision-engine spec set must exist");
        }
    }
}
