using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Indexers.Comix;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.Comix
{
    /// <summary>
    /// Fixture for <see cref="ComixRequestGenerator"/>.
    ///
    /// <para>
    /// Phase 17 (Plan 17-02 Task 2c per revision iteration 1, B-4): pre-existing
    /// <c>&amp;_=</c> token-presence assertions DELETED + REPLACED with
    /// <see cref="ComixRequestGenerator.ResolvedSignerPaths"/> assertions per Path A
    /// (signer-returns-decoded-JSON; URL-with-token composition is dead). Structural
    /// assertions on <c>order%5Bnumber%5D=desc</c> / <c>limit=100</c> / <c>&amp;number=</c>
    /// chapter-filter / etc. are PRESERVED — they apply to the path string returned by
    /// <see cref="ComixRequestGenerator.BuildChapterListPath"/> just as they did to the
    /// pre-Phase-17 URL.
    /// </para>
    ///
    /// <para>
    /// Audit trail for the Path A rewrite (deleted / renamed / replaced tests) lives in
    /// <c>17-02-SUMMARY.md</c> handoff (Task 2c step 1).
    /// </para>
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

            // Phase 17 D-16: Mock<IComixSigner> registered for any test that resolves
            // ComixRequestGenerator via Mocker.Resolve — keeps consumers happy if they
            // exercise Subject.Signer indirectly. (Direct callers of GetSearchRequests
            // don't reach the signer; ComixIndexer.Fetch dispatches it from outside.)
            const string CannedToken = "{\"result\":{\"items\":[]}}";
            Mocker.GetMock<IComixSigner>()
                  .Setup(s => s.ProxyFetchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(CannedToken);
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
            // generator emits an empty chain so dispatch short-circuits.
            var manga = new Manga.Manga { Title = "One Piece" };
            var criteria = new MangaSearchCriteria { Manga = manga };

            var chain = Subject.GetSearchRequests(criteria);

            chain.GetAllTiers().Should().BeEmpty();
        }

        [Test]
        public void GetSearchRequests_with_resolved_hash_populates_ResolvedSignerPaths_with_chapter_list_path()
        {
            // Phase 17 Path A — replaces the pre-Phase-17 URL-with-token assertion. The
            // chain itself is empty now (ComixIndexer.Fetch reads ResolvedSignerPaths and
            // dispatches via _signer.ProxyFetchAsync); structural assertions move to the
            // ResolvedSignerPaths[0] string.
            Subject.ResolvedMangaHash = "mr3m0";
            Subject.ResolvedMangaSlug = "mr3m0-the-forgotten-field";

            var manga = new Manga.Manga { Title = "The Forgotten Field" };
            var criteria = new MangaSearchCriteria { Manga = manga };

            var chain = Subject.GetSearchRequests(criteria);

            chain.GetAllTiers().Should().BeEmpty(
                "Phase 17 Path A: ComixIndexer.Fetch reads ResolvedSignerPaths instead of dispatching IndexerRequests");

            Subject.ResolvedSignerPaths.Should().ContainSingle()
                   .Which.Should().StartWith("/manga/mr3m0/chapters")
                   .And.NotContain("&_=", "Phase 17 Path A: token query param removed; signer applies inside page context")
                   .And.Contain("order%5Bnumber%5D=desc")
                   .And.Contain("limit=100")
                   .And.Contain("page=1")
                   .And.Contain("mangaSlug=");
        }

        [Test]
        public void GetSearchRequests_ChapterSearchCriteria_appends_number_filter()
        {
            // comix.to /api/v1/manga/{hid}/chapters supports an undocumented &number={N}
            // server-side filter (verified live 2026-05-08). Chapter-scope searches must
            // append this so the response is bounded to the requested chapter. Phase 17:
            // assertion shifts from URL to ResolvedSignerPaths[0] per Path A.
            Subject.ResolvedMangaHash = "mr3m0";
            Subject.ResolvedMangaSlug = "mr3m0-the-forgotten-field";

            var manga = new Manga.Manga { Title = "The Forgotten Field" };
            var chapter = new Manga.Chapter { ChapterNumber = 4m };
            var criteria = new ChapterSearchCriteria
            {
                Manga = manga,
                Chapters = new List<Manga.Chapter> { chapter }
            };

            Subject.GetSearchRequests(criteria);

            Subject.ResolvedSignerPaths.Should().ContainSingle()
                   .Which.Should().StartWith("/manga/mr3m0/chapters")
                   .And.Contain("number=4")
                   .And.NotContain("&_=", "Phase 17 Path A: token query param removed")
                   .And.Contain("mangaSlug=");
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
                Chapters = new List<Manga.Chapter> { chapter }
            };

            Subject.GetSearchRequests(criteria);

            Subject.ResolvedSignerPaths.Single().Should().Contain("number=12.5");
        }

        [Test]
        public void GetSearchRequests_ChapterSearchCriteria_returns_empty_when_no_resolved_hash()
        {
            // ChapterSearchCriteria still requires a resolved hid. ComixIndexer.Fetch() runs
            // the title→hid lookup before invoking the generator; if that lookup yields no
            // match, the chain is empty so dispatch short-circuits.
            var manga = new Manga.Manga { Title = "Unmappable Manga" };
            var chapter = new Manga.Chapter { ChapterNumber = 1m };
            var criteria = new ChapterSearchCriteria
            {
                Manga = manga,
                Chapters = new List<Manga.Chapter> { chapter }
            };

            var chain = Subject.GetSearchRequests(criteria);

            chain.GetAllTiers().Should().BeEmpty();
            Subject.ResolvedSignerPaths.Should().BeEmpty(
                "no resolved hid → no signer paths populated");
        }

        [Test]
        public void BuildChapterListPath_without_chapter_number_omits_number_filter()
        {
            // Manga (whole-feed) searches must NOT add &number= — they need the full
            // chapter list. Backwards-compat with the existing MangaSearchCriteria path.
            var path = Subject.BuildChapterListPath("mr3m0", "mr3m0-the-forgotten-field");
            path.Should().NotContain("&number=");
            path.Should().NotContain("&_=", "Phase 17 Path A: token query param removed");
        }
    }
}
