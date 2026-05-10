using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Comix;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.Comix
{
    /// <summary>
    /// Phase 4 plan 04-02 Task 2 (Phase 17 D-08 + Phase 17.2 GAP-17-E update) — verifies
    /// <see cref="ComixIndexer.GetChapterPages"/> assembles per-page descriptors from
    /// the synthesized chapter-detail response shape.
    ///
    /// <para>
    /// Phase 17.2 GAP-17-E (2026-05-10): the legacy shape was
    /// <c>/api/v1/chapters/{id}/pages</c> -> <c>{status, result:{images:[{url:absolute}]}}</c>;
    /// the bundle's signer allowlist now rejects all <c>/chapters/{id}/&lt;suffix&gt;</c>
    /// shapes — the new shape is <c>/api/v1/chapters/{id}</c> -> chapter detail with
    /// pages embedded under <c>result.pages.{baseUrl, items[]}</c>. Per-image absolute URLs
    /// are composed by concatenating <c>baseUrl + items[i].url</c>. See
    /// <c>.planning/phases/17.2-comix-signer-driver-layer-fix/17.2-PAGES-ENDPOINT-SURVEY.md</c>
    /// for survey evidence.
    /// </para>
    ///
    /// <para>
    /// Phase 17 (Plan 17-02 Task 2c per revision iteration 1, B-4): the chapter-pages
    /// endpoint is signed per RESEARCH N-3, so the fixture asserts on the signer dispatch
    /// rather than the legacy IHttpClient request shape. Per-image CDN GETs (cdn.comix.to
    /// or wowpic-style host) stay UNCHANGED — those tests live in the Phase 4
    /// ChapterPageFetcher fixtures, not here.
    /// </para>
    ///
    /// <para>
    /// Pre-Phase-17 assertions DELETED (the manifest endpoint no longer goes through
    /// IHttpClient): <c>RateLimitKey_set_to_comix_to_SourceKey</c>,
    /// <c>Honest_UserAgent_applied</c>, <c>Referer_header_set_to_comix_base_url</c>.
    /// Replaced by <c>Signer_dispatched_with_path_only_for_chapter_id</c> which checks the
    /// signer received the right path-only string (the signer applies its own in-page
    /// Referer/UA via the warm Chromium session it owns).
    /// </para>
    /// </summary>
    [TestFixture]
    public class ComixGetChapterPagesFixture : CoreTest<ComixIndexer>
    {
        private string _pagesJson;
        private string _capturedSignerPath;

        [SetUp]
        public void Setup()
        {
            _capturedSignerPath = null;
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

            // Phase 17 D-08: GetChapterPages now dispatches via _signer.ProxyFetchAsync.
            // Capture the path the signer was called with so the dispatch test can assert
            // on it directly.
            Mocker.GetMock<IComixSigner>()
                  .Setup(s => s.ProxyFetchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .Returns<string, CancellationToken>((path, _) =>
                  {
                      _capturedSignerPath = path;
                      return Task.FromResult(_pagesJson);
                  });
        }

        private static ReleaseInfo BuildRelease()
            => new ReleaseInfo
            {
                // Phase 17.2 GAP-17-E: DownloadUrl no longer carries the /pages suffix
                // (per ComixParser.cs DownloadUrl construction post-survey-winner).
                DownloadUrl = "https://comix.to/api/v1/chapters/12345",
                ScanlationGroup = null   // typical for official rows
            };

        [Test]
        public async Task Signer_dispatched_with_path_only_for_chapter_id()
        {
            // Phase 17 D-08 + Phase 17.2 GAP-17-E: signer receives the API path WITHOUT the
            // /api/v1 prefix (the in-page proxyFetch JS reapplies that prefix). Post-Phase-17.2
            // survey, the path is the bare /chapters/{id} (no /pages suffix) — the bundle's
            // signer allowlist rejects all /chapters/{id}/<suffix> shapes since 2026-05-10.
            await Subject.GetChapterPages(BuildRelease());

            _capturedSignerPath.Should().Be("/chapters/12345",
                "GetChapterPages strips the /api/v1 prefix before signing; Phase 17.2 " +
                "GAP-17-E winner shape per 17.2-PAGES-ENDPOINT-SURVEY.md is /chapters/{id}.");
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
