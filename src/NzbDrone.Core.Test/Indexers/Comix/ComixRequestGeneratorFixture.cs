using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers.Comix;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.Comix
{
    /// <summary>
    /// Fixture for <see cref="ComixRequestGenerator"/>.
    ///
    /// Coverage:
    /// - URL composition for /api/v1/manga (FetchRecent / latest_updates ordering)
    /// - URL composition for /api/v1/manga/{hid}/chapters (per-manga chapter list)
    ///   includes the keiyoushi <c>_=</c> anti-bot token
    /// - Search request returns an empty chain when no <see cref="ComixRequestGenerator.ResolvedMangaHash"/>
    ///   is set (the indexer resolves title→hid before invoking the generator)
    ///
    /// Updated 2026-05-08 (comix-indexer-404 debug session): pivoted from
    /// /api/v2/manga/{slug}/chapters to /api/v1/manga/{hid}/chapters with hash token,
    /// matching keiyoushi's current Comix.kt + the live API verified during the bug investigation.
    /// </summary>
    [TestFixture]
    public class ComixRequestGeneratorFixture : CoreTest<ComixRequestGenerator>
    {
        [SetUp]
        public void Setup()
        {
            Subject.Settings = new ComixIndexerSettings
            {
                BaseUrl = "https://comix.to",
                SourceKey = "comix.to"
            };
        }

        [Test]
        public void GetRecentRequests_targets_manga_listing_with_chapter_updated_at_desc()
        {
            var chain = Subject.GetRecentRequests();
            var url = chain.GetAllTiers().First().First().Url.FullUri;
            url.Should().Contain("/api/v1/manga");

            // bracket query params are URL-encoded on the wire to keep the request line valid;
            // comix.to un-encodes them server-side.
            url.Should().Contain("order%5Bchapter_updated_at%5D=desc");
        }

        [Test]
        public void GetSearchRequests_with_unresolved_hash_returns_empty_chain()
        {
            // ComixIndexer.Fetch() resolves Manga.Title -> hid via /api/v1/manga?keyword=...
            // before invoking the request generator. Without ResolvedMangaHash set, the
            // generator emits an empty chain so FetchReleases short-circuits.
            var manga = new Manga.Manga { Title = "One Piece" };
            var criteria = new MangaSearchCriteria { Manga = manga };

            var chain = Subject.GetSearchRequests(criteria);

            chain.GetAllTiers().Should().BeEmpty();
        }

        [Test]
        public void GetSearchRequests_with_resolved_hash_targets_v1_chapters_with_token()
        {
            Subject.ResolvedMangaHash = "mr3m0";
            Subject.ResolvedMangaSlug = "mr3m0-the-forgotten-field";

            var manga = new Manga.Manga { Title = "The Forgotten Field" };
            var criteria = new MangaSearchCriteria { Manga = manga };

            var chain = Subject.GetSearchRequests(criteria);

            chain.GetAllTiers().Should().NotBeEmpty();
            var url = chain.GetAllTiers().First().First().Url.FullUri;
            url.Should().Contain("/api/v1/manga/mr3m0/chapters");
            url.Should().Contain("order%5Bnumber%5D=desc");
            url.Should().Contain("limit=100");
            url.Should().Contain("page=1");
            url.Should().Contain("_=");          // anti-bot token query param required by comix.to
            url.Should().Contain("mangaSlug=");
        }

        [Test]
        public void BuildChapterListUrl_token_is_deterministic_for_same_path()
        {
            // The hash token derives from the URL path — same path -> same token. This makes
            // unit tests reproducible across runs. (We intentionally do NOT pin the EXACT
            // token value here because that would couple the test to ComixHash internals.)
            var u1 = Subject.BuildChapterListUrl("mr3m0", "mr3m0-the-forgotten-field");
            var u2 = Subject.BuildChapterListUrl("mr3m0", "mr3m0-the-forgotten-field");
            u1.Should().Be(u2);
        }

        [Test]
        public void GetSearchRequests_ChapterSearchCriteria_appends_number_filter()
        {
            // comix.to /api/v1/manga/{hid}/chapters supports an undocumented &number={N}
            // server-side filter (verified live 2026-05-08). Chapter-scope searches must
            // append this so the response is bounded to the requested chapter.
            Subject.ResolvedMangaHash = "mr3m0";
            Subject.ResolvedMangaSlug = "mr3m0-the-forgotten-field";

            var manga = new Manga.Manga { Title = "The Forgotten Field" };
            var chapter = new Manga.Chapter { ChapterNumber = 4m, TranslatedLanguage = "en" };
            var criteria = new ChapterSearchCriteria
            {
                Manga = manga,
                Chapters = new System.Collections.Generic.List<Manga.Chapter> { chapter }
            };

            var chain = Subject.GetSearchRequests(criteria);
            chain.GetAllTiers().Should().NotBeEmpty();

            var url = chain.GetAllTiers().First().First().Url.FullUri;
            url.Should().Contain("/api/v1/manga/mr3m0/chapters");
            url.Should().Contain("number=4");
            url.Should().Contain("_=");          // anti-bot token still present
            url.Should().Contain("mangaSlug=");
        }

        [Test]
        public void GetSearchRequests_ChapterSearchCriteria_with_decimal_chapter_round_trips()
        {
            Subject.ResolvedMangaHash = "mr3m0";
            Subject.ResolvedMangaSlug = "mr3m0-the-forgotten-field";

            var manga = new Manga.Manga { Title = "The Forgotten Field" };
            var chapter = new Manga.Chapter { ChapterNumber = 12.5m };
            var criteria = new ChapterSearchCriteria
            {
                Manga = manga,
                Chapters = new System.Collections.Generic.List<Manga.Chapter> { chapter }
            };

            var chain = Subject.GetSearchRequests(criteria);
            var url = chain.GetAllTiers().First().First().Url.FullUri;

            url.Should().Contain("number=12.5");
        }

        [Test]
        public void GetSearchRequests_ChapterSearchCriteria_returns_empty_when_no_resolved_hash()
        {
            // ChapterSearchCriteria still requires a resolved hid. ComixIndexer.Fetch() runs
            // the title→hid lookup before invoking the generator; if that lookup yields no
            // match, the chain is empty so FetchReleases short-circuits.
            var manga = new Manga.Manga { Title = "Unmappable Manga" };
            var chapter = new Manga.Chapter { ChapterNumber = 1m };
            var criteria = new ChapterSearchCriteria
            {
                Manga = manga,
                Chapters = new System.Collections.Generic.List<Manga.Chapter> { chapter }
            };

            var chain = Subject.GetSearchRequests(criteria);

            chain.GetAllTiers().Should().BeEmpty();
        }

        [Test]
        public void BuildChapterListUrl_without_chapter_number_omits_number_filter()
        {
            // Manga (whole-feed) searches must NOT add &number= — they need the full chapter
            // list. Backwards-compat with the existing MangaSearchCriteria + RSS path.
            var url = Subject.BuildChapterListUrl("mr3m0", "mr3m0-the-forgotten-field");
            url.Should().NotContain("&number=");
        }
    }
}
