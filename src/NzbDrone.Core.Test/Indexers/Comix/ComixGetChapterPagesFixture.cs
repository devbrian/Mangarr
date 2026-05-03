using System.IO;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Comix;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.Comix
{
    /// <summary>
    /// Phase 4 plan 04-02 Task 2 — verifies <see cref="ComixIndexer.GetChapterPages"/> assembles
    /// per-page descriptors from the synthesized <c>/api/v2/chapters/{id}</c> response shape
    /// (loads <c>Files/Phase4/comix_chapter_pages.json</c>; live capture blocked by Cloudflare
    /// per Phase 3 LEARNINGS — synthesized fixture contract per SOURCE-PROBE-fixtures.md).
    ///
    /// <para>
    /// Pitfall 1 / F-01 class regression guard: every captured request MUST have
    /// <c>RateLimitKey="comix.to"</c>. Comix-specific contract: ExpiresAt == null because
    /// comix.to URLs are durable; D-03 re-fetch is a never-fired safety net.
    /// </para>
    /// </summary>
    [TestFixture]
    public class ComixGetChapterPagesFixture : CoreTest<ComixIndexer>
    {
        private HttpRequest _capturedRequest;
        private string _pagesJson;

        [SetUp]
        public void Setup()
        {
            _capturedRequest = null;
            _pagesJson = File.ReadAllText("Files/Phase4/comix_chapter_pages.json");

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

            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.GetAsync<ComixChapterPagesResponse>(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(req =>
                  {
                      _capturedRequest = req;
                      var headers = new HttpHeader { ContentType = "application/json" };
                      var raw = new HttpResponse(req, headers, _pagesJson, HttpStatusCode.OK);
                      return Task.FromResult(new HttpResponse<ComixChapterPagesResponse>(raw));
                  });
        }

        private static ReleaseInfo BuildRelease()
            => new ReleaseInfo
            {
                DownloadUrl = "https://comix.to/api/v2/chapters/12345",
                ScanlationGroup = null   // typical for official rows
            };

        [Test]
        public async Task RateLimitKey_set_to_comix_to_SourceKey()
        {
            await Subject.GetChapterPages(BuildRelease());

            _capturedRequest.Should().NotBeNull();
            _capturedRequest.RateLimitKey.Should().Be("comix.to");
        }

        [Test]
        public async Task Honest_UserAgent_applied()
        {
            await Subject.GetChapterPages(BuildRelease());

            _capturedRequest.Headers["User-Agent"].Should().StartWith("Mangarr/");
        }

        [Test]
        public async Task Referer_header_set_to_comix_base_url()
        {
            // Phase 3 D-Comix Cloudflare Pitfall — keiyoushi mandates Referer.
            await Subject.GetChapterPages(BuildRelease());

            _capturedRequest.Headers["Referer"].Should().Be("https://comix.to/");
        }

        [Test]
        public async Task ExpiresAt_is_null_for_durable_urls()
        {
            // Comix-specific contract: durable URLs; ExpiresAt left null so
            // ChapterDownloadService doesn't speculatively re-fetch.
            var manifest = await Subject.GetChapterPages(BuildRelease());

            manifest.ExpiresAt.Should().BeNull();
        }

        [Test]
        public async Task Pages_preserve_array_order()
        {
            var manifest = await Subject.GetChapterPages(BuildRelease());

            manifest.Pages.Should().HaveCount(8);
            for (var i = 0; i < manifest.Pages.Count; i++)
            {
                manifest.Pages[i].PageIndex.Should().Be(i + 1);
                manifest.Pages[i].Url.Should().Be($"https://cdn.comix.to/manga/test/ch1/{i + 1}.jpg");
            }
        }

        [Test]
        public async Task TotalCount_matches_pages_array_length()
        {
            var manifest = await Subject.GetChapterPages(BuildRelease());

            manifest.TotalCount.Should().Be(8);
        }
    }
}
