using System.IO;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.MangaDex;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.MangaDex
{
    /// <summary>
    /// Phase 4 plan 04-02 Task 2 — verifies <see cref="MangaDexIndexer.GetChapterPages"/> assembles
    /// per-page URLs from the live <c>/at-home/server/{chapterId}</c> response shape (loads
    /// <c>Files/Phase4/manga_dex_at_home_server.json</c>).
    ///
    /// <para>
    /// Pitfall 1 / F-01 class regression guard: every captured request MUST have
    /// <c>RateLimitKey="mangadex"</c>. Pitfall (token leakage): override sends NO Authorization
    /// header on the at-home GET (the URL is published — sending creds leaks them to the
    /// community CDN).
    /// </para>
    /// </summary>
    [TestFixture]
    public class MangaDexGetChapterPagesFixture : CoreTest<MangaDexIndexer>
    {
        private HttpRequest _capturedRequest;
        private string _atHomeJson;

        [SetUp]
        public void Setup()
        {
            _capturedRequest = null;
            _atHomeJson = File.ReadAllText("Files/Phase4/manga_dex_at_home_server.json");

            Subject.Definition = new IndexerDefinition
            {
                Id = 1,
                Name = "MangaDex",
                Settings = new MangaDexIndexerSettings
                {
                    BaseUrl = "https://api.mangadex.org",
                    SourceKey = "mangadex"
                }
            };

            // GetAsync<T> path: capture the request and return a typed HttpResponse<T> built
            // from the canonical fixture body.
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.GetAsync<MangaDexAtHomeResource>(It.IsAny<HttpRequest>()))
                  .Returns<HttpRequest>(req =>
                  {
                      _capturedRequest = req;
                      var headers = new HttpHeader { ContentType = "application/json" };
                      var raw = new HttpResponse(req, headers, _atHomeJson, HttpStatusCode.OK);
                      return Task.FromResult(new HttpResponse<MangaDexAtHomeResource>(raw));
                  });
        }

        private static ReleaseInfo BuildRelease(string scanlationGroup = "TestGroup")
            => new ReleaseInfo
            {
                DownloadUrl = "https://api.mangadex.org/at-home/server/abc123",
                ScanlationGroup = scanlationGroup
            };

        [Test]
        public async Task RateLimitKey_set_to_mangadex_SourceKey()
        {
            // Pitfall 1 / F-01 class guard — explicit assignment at the call site.
            await Subject.GetChapterPages(BuildRelease());

            _capturedRequest.Should().NotBeNull();
            _capturedRequest.RateLimitKey.Should().Be("mangadex");
        }

        [Test]
        public async Task Honest_UserAgent_applied()
        {
            await Subject.GetChapterPages(BuildRelease());

            _capturedRequest.Headers["User-Agent"].Should().StartWith("Mangarr/");
        }

        [Test]
        public async Task No_Authorization_header_sent_to_at_home_server()
        {
            // Token leakage Pitfall: at-home URLs are CDN-hosted; sending Authorization would
            // leak credentials to community nodes. Verified by ensuring no Authorization header
            // gets written by the override.
            await Subject.GetChapterPages(BuildRelease());

            _capturedRequest.Headers["Authorization"].Should().BeNull();
        }

        [Test]
        public async Task Pages_assembled_as_baseUrl_data_hash_filename()
        {
            // Production-trusted URL form: {baseUrl}/data/{hash}/{filename}.
            var manifest = await Subject.GetChapterPages(BuildRelease());

            manifest.Should().NotBeNull();
            manifest.Pages.Should().NotBeEmpty();
            manifest.Pages[0].Url.Should().StartWith("https://uploads.mangadex.org/data/1c2c1cd0aaaabbbbccccddddeeeeffff/");
            manifest.Pages[0].PageIndex.Should().Be(1);
        }

        [Test]
        public async Task TotalCount_matches_data_array_length()
        {
            var manifest = await Subject.GetChapterPages(BuildRelease());

            manifest.TotalCount.Should().Be(8);
            manifest.Pages.Should().HaveCount(8);
        }

        [Test]
        public async Task PageIndex_is_1_based_and_in_order()
        {
            var manifest = await Subject.GetChapterPages(BuildRelease());

            for (var i = 0; i < manifest.Pages.Count; i++)
            {
                manifest.Pages[i].PageIndex.Should().Be(i + 1);
            }
        }

        [Test]
        public async Task ExpiresAt_is_set_to_about_15_minutes_from_now()
        {
            // Documented MangaDex token TTL ~15min; D-03 reactive 403/410 → re-fetch handles
            // expiry, so this is informational. We assert ExpiresAt is set and is in the future
            // within a generous window.
            var before = System.DateTimeOffset.UtcNow;
            var manifest = await Subject.GetChapterPages(BuildRelease());

            manifest.ExpiresAt.Should().NotBeNull();
            manifest.ExpiresAt.Value.Should().BeAfter(before.AddMinutes(14));
            manifest.ExpiresAt.Value.Should().BeBefore(before.AddMinutes(16));
        }

        [Test]
        public async Task ScanlationGroup_carried_from_ReleaseInfo()
        {
            var manifest = await Subject.GetChapterPages(BuildRelease("ScanGroupX"));

            manifest.ScanlationGroup.Should().Be("ScanGroupX");
        }
    }
}
