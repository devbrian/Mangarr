using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers.Comix;
using NzbDrone.Test.Common;
using NzbDrone.Test.Common.Categories;

namespace Mangarr.Comix.Live.Test
{
    /// <summary>
    /// Phase 17 D-18 — live-Chromium acceptance bar. Excluded from standard
    /// scripts/test.sh runs via Category=LiveComix exclusion (Plan 17-01 Task 2);
    /// runs locally on developer machines AND in the Plan 03-06 daily-soak workflow
    /// (Plan 17-04 Task 2). CI never runs these per D-17.
    /// </summary>
    // Sonarr divergence: no Sonarr peer. Live-Chromium fixture is a forced manga-side
    // divergence — comix.to encrypts response bodies + rotates anti-bot signer-fn names
    // per deploy, so a runtime browser-driven signer is the only structurally viable shape.
    // See .planning/phases/17-comix-runtime-signer-port-puppeteersharp/17-CONTEXT.md D-17/D-18.
    //
    // Implementation note (Plan 17-04 Task 1, deviation Rule 3): plan body literal said
    // `CoreTest<ComixPuppeteerSigner>` but `CoreTest<T>` lives in `Mangarr.Core.Test.csproj`
    // (NOT `Mangarr.Test.Common.csproj`); referencing the Core test project from a
    // sibling test project introduces brittle test-resource bleed. Switched to
    // `TestBase<TSubject>` (`NzbDrone.Test.Common`) which provides `Subject` + `Mocker`
    // identically to CoreTest<T> for the live-fixture purpose (auto-resolve the
    // ComixPuppeteerSigner subject via AutoMoq, then exercise its real Chromium child
    // against live comix.to).
    [TestFixture]
    [LiveComix]
    public class ComixSignerLiveFixture : TestBase<ComixPuppeteerSigner>
    {
        [Test]
        public async Task ProxyFetchManga_returns_decoded_JSON_with_chapters_array()
        {
            var json = await Subject.ProxyFetchAsync("/manga/mr3m0/chapters");
            json.Should().NotBeNullOrWhiteSpace();
            json.Should().Contain("\"items\"", "decoded body shape per ComixDto.cs");
        }

        [Test]
        public async Task ProxyFetchPages_returns_decoded_JSON_with_pages_object()
        {
            // Phase 17.2 GAP-17-E (2026-05-10): the bundle's signer allowlist no longer
            // accepts /chapters/{id}/<suffix> shapes (per 17.2-PAGES-ENDPOINT-SURVEY.md
            // winner verdict). The bare /chapters/{id} endpoint returns 200 with the
            // pages list embedded under `result.pages.{baseUrl, items[]}` in the
            // production decrypt-wrap envelope.
            var chaptersJson = await Subject.ProxyFetchAsync("/manga/mr3m0/chapters");
            var chapterId = ExtractFirstChapterId(chaptersJson);
            chapterId.Should().NotBeNullOrEmpty("chapter list must contain at least one row");

            var pagesJson = await Subject.ProxyFetchAsync($"/chapters/{chapterId}");
            pagesJson.Should().NotBeNullOrWhiteSpace();
            pagesJson.Should().Contain("\"pages\"",
                "decoded body shape per ComixChapterPagesResponse — chapter detail embeds " +
                "the pages list under result.pages.{baseUrl, items[]} per Phase 17.2 GAP-17-E " +
                "winner verdict (17.2-PAGES-ENDPOINT-SURVEY.md). The legacy `\"images\"` shape " +
                "was retired alongside response-body encryption + per-deploy signer rotation.");
        }

        [OneTimeTearDown]
        public void TearDownLiveSigner()
        {
            Subject?.Dispose();
        }

        private static string ExtractFirstChapterId(string json)
        {
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Investigation Phase 3 (2026-05-23) oracle pivot: the env-module-oracle
            // returns the unwrapped `result` object directly (the bundle's ok+result
            // unwrap interceptor strips the `{status:"ok", result:{...}}` envelope).
            // Older Phase 17.2 shape kept the envelope intact; we accept both for
            // forward compatibility across rotations.
            if (root.TryGetProperty("items", out var topItems))
            {
                if (topItems.GetArrayLength() > 0
                    && topItems[0].TryGetProperty("id", out var idEl1))
                {
                    return idEl1.ValueKind == System.Text.Json.JsonValueKind.String
                        ? idEl1.GetString()
                        : idEl1.GetRawText();
                }

                return null;
            }

            if (root.TryGetProperty("result", out var result)
                && result.TryGetProperty("items", out var items)
                && items.GetArrayLength() > 0
                && items[0].TryGetProperty("id", out var idEl2))
            {
                return idEl2.ValueKind == System.Text.Json.JsonValueKind.String
                    ? idEl2.GetString()
                    : idEl2.GetRawText();
            }

            return null;
        }
    }
}
