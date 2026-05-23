using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Comix;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Test.Common;
using NzbDrone.Test.Common.Categories;

namespace Mangarr.Comix.Live.Test
{
    /// <summary>
    /// End-to-end live acceptance test for <see cref="ComixIndexer.Fetch(MangaSearchCriteria)"/>
    /// against real comix.to. Composes the real <see cref="ComixPuppeteerSigner"/> +
    /// real <see cref="ComixIndexer"/> (with AutoMoq-resolved dependencies for the non-
    /// signer leg — <c>IIndexerStatusService</c>, <c>IIndexerSourceStatusService</c>,
    /// <c>IConfigService</c>, <c>IMangaParsingService</c>, <c>IHttpClient</c>; the indexer
    /// only delegates the signer leg to live Chromium, the rest is exercised in unit
    /// fixtures with mocks).
    ///
    /// <para>
    /// This fixture is the live counterpart to the unit-side
    /// <c>Fetch_MangaSearchCriteria_produces_releases_when_signer_returns_unwrapped_env_module_shapes</c>
    /// test on <c>ComixIndexerFixture</c>. It proves the env-module-oracle cascade
    /// fix (commit e02af4cb2) works against real comix.to: keyword-search returns a
    /// real hid, the chapter-list dispatch returns a real (decrypted) chapter list,
    /// and the parser composes both into a non-empty <c>IList&lt;ReleaseInfo&gt;</c>.
    /// </para>
    ///
    /// <para>
    /// Pre-fix, this test would have caught the user-reported "interactive search
    /// returned 0 Comix results for 'The Greatest Estate Developer'" bug — the
    /// existing live fixtures only asserted the raw signer body contained the
    /// substring <c>"items"</c>, so neither the Cloudflare 403 on plain GETs nor
    /// the parser's wrapped-only probe path surfaced as a test failure.
    /// </para>
    ///
    /// <para>
    /// <see cref="LiveComixAttribute"/> only (matches <see cref="ComixSignerLiveFixture"/>
    /// convention — NUnit's <c>[Explicit]</c> would block discovery on
    /// <c>--filter "Category=LiveComix"</c> runs because Explicit tests are excluded
    /// from non-Explicit runs even when category-filtered). Standard scripts/test.sh
    /// runs exclude <c>Category=LiveComix</c>; CI never runs these; developers run
    /// locally + the Plan 03-06 daily-soak workflow runs them on schedule (Plan 17-04
    /// Task 2).
    /// </para>
    /// </summary>
    // Sonarr divergence: no Sonarr peer. Live-Chromium integration fixture is a forced
    // manga-side divergence — comix.to encrypts response bodies + rotates anti-bot
    // signer-fn names per deploy, so a runtime browser-driven signer is the only
    // structurally viable shape. See .planning/phases/17-comix-runtime-signer-port-puppeteersharp/17-CONTEXT.md
    // D-17/D-18.
    [TestFixture]
    [LiveComix]
    public class ComixIndexerLiveFixture : TestBase<ComixIndexer>
    {
        private ComixPuppeteerSigner _signer;

        [SetUp]
        public void SetUpRealSigner()
        {
            // Compose the REAL signer (AutoMoq-resolved deps for IIndexerSourceStatusService
            // + Logger) and inject it into the Mocker so Subject.Fetch dispatches against
            // a live Chromium child against real comix.to. The other ComixIndexer ctor deps
            // (IIndexerStatusService, IConfigService, IMangaParsingService, IHttpClient,
            // ILocalizationService) stay AutoMoq-mocked — Fetch(MangaSearchCriteria) only
            // touches the signer leg + the parser (zero IHttpClient hits because the
            // hid-resolver and chapter-list dispatch BOTH go through the signer now per
            // the 2026-05-23 env-module-oracle cascade fix).
            _signer = Mocker.Resolve<ComixPuppeteerSigner>();
            Mocker.SetConstant<IComixSigner>(_signer);

            Subject.Definition = new IndexerDefinition
            {
                Id = 2,
                Name = "Comix",
                Settings = new ComixIndexerSettings
                {
                    BaseUrl = "https://comix.to",
                    SourceKey = "comix.to"
                }
            };
        }

        [OneTimeTearDown]
        public void TearDownLiveSigner()
        {
            _signer?.Dispose();
        }

        [Test]
        public async Task Fetch_for_The_Forgotten_Field_produces_non_empty_release_list()
        {
            // "The Forgotten Field" is the Phase 3 canonical live target (hid "mr3m0")
            // — used by ComixSignerLiveFixture.ProxyFetchManga_returns_decoded_JSON_with_chapters_array
            // and ProxyFetchKeywordSearch_returns_decoded_JSON_with_items_array. Exercising
            // it through ComixIndexer.Fetch composes the keyword-search + chapter-list
            // legs into a real end-to-end assertion.
            var criteria = new MangaSearchCriteria
            {
                Manga = new NzbDrone.Core.Manga.Manga
                {
                    Title = "The Forgotten Field",
                    CleanTitle = "theforgottenfield"
                }
            };

            var releases = await Subject.Fetch(criteria);

            releases.Should().NotBeNull();
            releases.Should().NotBeEmpty(
                "env-module-oracle end-to-end: keyword-search MUST resolve a hid (signer dispatch, NOT a plain GET that Cloudflare 403s) AND chapter-list parser MUST accept the unwrapped {items, meta} shape. Pre-fix commit e02af4cb2 this returned 0 — the bug the user reported for 'The Greatest Estate Developer'.");

            // State-not-just-rendering assertions per feedback_verify_ui_state_not_just_rendering:
            // - Real DownloadUrl (not null / placeholder)
            // - Title contains the manga name (EnrichTitlesWithMangaName prefixed it)
            // - DownloadUrl is a comix.to URL (proves the parser composed it, not a stub)
            // - DownloadProtocol is HTTP (manga aggregator default)
            releases.Should().OnlyContain(r => r.DownloadProtocol == DownloadProtocol.Http);
            releases.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.DownloadUrl));
            releases.Should().OnlyContain(r => r.DownloadUrl.Contains("comix.to/api/v1/chapters/"));
            releases.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.Title));
            releases.Should().Contain(r => r.Title.Contains("The Forgotten Field"),
                "EnrichTitlesWithMangaName must prefix the manga title (chapter-list endpoint is keyed per-manga; without the prefix downstream MangaParser.ParseChapterTitle can't resolve and every release rejects with 'Unknown Manga').");
        }

        [Test]
        public async Task Fetch_for_The_Greatest_Estate_Developer_produces_non_empty_release_list()
        {
            // The user-reported failure case from 2026-05-22 interactive search: comix.to
            // hosts this title with many chapters, but pre-fix the Fetch call returned 0
            // releases because of the cascade (plain-GET 403 + wrapped-only parser probe).
            // Asserting non-empty here directly proves the user's "0 results" bug is gone.
            var criteria = new MangaSearchCriteria
            {
                Manga = new NzbDrone.Core.Manga.Manga
                {
                    Title = "The Greatest Estate Developer",
                    CleanTitle = "thegreatestestatedeveloper"
                }
            };

            var releases = await Subject.Fetch(criteria);

            releases.Should().NotBeNull();
            releases.Should().NotBeEmpty(
                "user-reported failure case: 'The Greatest Estate Developer' interactive search returned 0 Comix results pre-fix despite comix.to hosting the title. Non-empty here proves the cascade fix in commit e02af4cb2 works end-to-end against live comix.to.");

            // Same state assertions as the primary canonical-target test.
            releases.Should().OnlyContain(r => r.DownloadProtocol == DownloadProtocol.Http);
            releases.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.DownloadUrl));
            releases.Should().OnlyContain(r => r.DownloadUrl.Contains("comix.to/api/v1/chapters/"));
            releases.Should().Contain(r => r.Title.Contains("The Greatest Estate Developer"),
                "title-prefix enrichment must apply for the user's reported title too.");
        }
    }
}
