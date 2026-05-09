using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Comix;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.Comix
{
    /// <summary>
    /// Wave 0 stub fixture for <see cref="ComixIndexer"/>. References NOT-YET-BUILT production
    /// types (lands in Plan 03-05). Mirrors <c>MangaDexIndexerFixture</c> shape; differences:
    ///
    /// - <c>SourceKey == "comix.to"</c> (D-12 verbatim site-name convention)
    /// - <see cref="ComixIndexer.GetDownloadHeaders"/> returns <c>Referer: https://comix.to/</c>
    ///   (D-14 — required by upstream comix.to API per keiyoushi extensions PR #11658)
    /// - NO UA-hidden reflection test (comix.to allows UA override per RESEARCH §anti-bot)
    /// </summary>
    [TestFixture]
    public class ComixIndexerFixture : CoreTest<ComixIndexer>
    {
        [SetUp]
        public void Setup()
        {
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

        [Test]
        public void DefaultSourceKey_should_be_comix_to()
        {
            Subject.DefaultSourceKey.Should().Be("comix.to");
        }

        [Test]
        public void Protocol_should_be_Http()
        {
            Subject.Protocol.Should().Be(DownloadProtocol.Http);
        }

        [Test]
        public async Task Fetch_MangaSearchCriteria_calls_FetchReleases()
        {
            var criteria = new MangaSearchCriteria();
            var releases = await Subject.Fetch(criteria);
            releases.Should().NotBeNull();
        }

        [Test]
        public void GetDownloadHeaders_returns_Referer_for_comix()
        {
            // D-14: comix.to requires Referer header on chapter requests.
            var release = new ReleaseInfo();
            var headers = Subject.GetDownloadHeaders(release);
            headers.Should().ContainKey("Referer");
            headers["Referer"].Should().Be("https://comix.to/");
        }

        [Test]
        public void EnrichTitlesWithMangaName_prefixes_chapter_titles_lacking_manga_name()
        {
            // ComixParser.ParseChapterList emits titles like "Chapter 4 [en] [Group]" — the
            // chapter-list endpoint is keyed per-manga so the parent title is implicit. Without
            // a manga-name prefix, MangaParser.ParseChapterTitle can't extract a usable
            // MangaTitle and downstream lookup fails with "Unknown Manga".
            var releases = new System.Collections.Generic.List<ReleaseInfo>
            {
                new ReleaseInfo { Title = "Chapter 4 [en] [Thunderscans]" },
                new ReleaseInfo { Title = "Chapter 12.5 [en] [Asura Scans]" },
            };

            var enriched = ComixIndexer.EnrichTitlesWithMangaName(releases, "The Forgotten Field");

            enriched[0].Title.Should().Be("The Forgotten Field - Chapter 4 [en] [Thunderscans]");
            enriched[1].Title.Should().Be("The Forgotten Field - Chapter 12.5 [en] [Asura Scans]");
        }

        [Test]
        public void EnrichTitlesWithMangaName_skips_titles_already_carrying_manga_name()
        {
            // ParseMangaList path already prefixes the title (e.g., latest-updates feed). Don't
            // double-prefix when the manga name is already present.
            var releases = new System.Collections.Generic.List<ReleaseInfo>
            {
                new ReleaseInfo { Title = "The Forgotten Field - Chapter 7 [en]" },
            };

            var enriched = ComixIndexer.EnrichTitlesWithMangaName(releases, "The Forgotten Field");

            enriched[0].Title.Should().Be("The Forgotten Field - Chapter 7 [en]");
        }

        [Test]
        public void EnrichTitlesWithMangaName_no_op_when_manga_title_blank()
        {
            // Defensive: if the criteria carries no manga title (shouldn't happen post-Phase 6,
            // but keep the helper safe), leave the releases untouched.
            var releases = new System.Collections.Generic.List<ReleaseInfo>
            {
                new ReleaseInfo { Title = "Chapter 4 [en] [Group]" },
            };

            var enriched = ComixIndexer.EnrichTitlesWithMangaName(releases, null);

            enriched[0].Title.Should().Be("Chapter 4 [en] [Group]");
        }
    }
}
