using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers.Comix;
using NzbDrone.Test.Common;
using NzbDrone.Test.Common.Categories;

namespace Mangarr.Comix.Live.Test
{
    /// <summary>
    /// captureToken stepwise diagnostic — 2026-05-22 rewrite. Drives the production
    /// <see cref="ComixPuppeteerSigner.ProxyFetchAsync"/> against live comix.to and
    /// asserts the captureToken sequence completed at each step.
    ///
    /// <para>
    /// Replaces the pre-2026-05-22 PROBE_JS stepwise diagnostic (Plan 17-05) which
    /// drove four sub-probes (raw-eval / install-only / sign-no-fetch / fetch-no-decrypt)
    /// against a probed signer + installer fn ref pair. That shape is gone — the bundle
    /// no longer exposes signer / installer through globalThis. The new captureToken
    /// flow has different observable seams; this fixture asserts those.
    /// </para>
    ///
    /// <para>
    /// Sequence asserted (per upstream Comix.kt:414-471 / our ResolveCaptureRoute):
    /// </para>
    /// <list type="number">
    ///   <item>Step 1 — call <c>ProxyFetchAsync("/manga/{hid}/chapters")</c>. The page
    ///         loads <c>https://comix.to/title/{hid}</c>, the bundle bootstraps + fires
    ///         its own <c>/api/v1/manga/{hid}/chapters?_=&lt;token&gt;</c> request, the
    ///         interception handler captures the token, the relay fetch returns the
    ///         decoded JSON body.</item>
    ///   <item>Step 2 — call <c>ProxyFetchAsync($"/chapters/{chapterId}")</c> for the
    ///         first chapter ID. The page loads
    ///         <c>https://comix.to/chapters/{chapterId}</c>, bundle fires its own
    ///         <c>/api/v1/chapters/{chapterId}?_=&lt;token&gt;</c>, handler captures
    ///         token, relay fetch returns the pages-embedded chapter detail JSON.</item>
    /// </list>
    ///
    /// <para>
    /// Excluded from <c>bash scripts/test.sh</c> runs by [LiveComix]; runs locally OR
    /// in the daily-soak workflow. CI never runs it (D-17).
    /// </para>
    /// </summary>
    [TestFixture]
    [LiveComix]
    [Explicit("captureToken diagnostic harness — runs only when explicitly invoked or via daily-soak.")]
    public class ComixSignerStepwiseDiagnosticFixture : TestBase<ComixPuppeteerSigner>
    {
        // Known-good targets per 17-08-LIVE-VERIFICATION-EVIDENCE.md.
        private const string KnownGoodMangaHid = "mr3m0";

        [Test]
        public async Task Step_1_captureToken_route_manga_chapters_returns_decoded_chapter_list()
        {
            // The chapter-list route loads /title/{hid} and waits for the bundle's
            // outgoing /api/v1/manga/{hid}/chapters request to surface the `_=<token>`
            // query parameter. If this throws, the page loaded but the bundle never
            // fired its bootstrap API request (Cloudflare interstitial / UA block /
            // bundle bootstrap broken).
            var json = await Subject.ProxyFetchAsync($"/manga/{KnownGoodMangaHid}/chapters");

            json.Should().NotBeNullOrWhiteSpace(
                "Step 1: captureToken must surface the chapter-list JSON body. Empty/null " +
                "means the relay fetch returned an empty response — check page-context error " +
                "console or rate-limit/CF 403 in the signer log.");

            json.Should().Contain("\"items\"",
                "Step 1: chapter-list body shape per ComixDto.cs — `result.items[]`. If " +
                "this fails, comix.to may have re-shaped its response envelope; capture " +
                "the actual body for triage.");
        }

        [Test]
        public async Task Step_2_captureToken_route_chapters_detail_returns_pages_payload()
        {
            // Depends on Step 1 having captured a chapter ID. Run as a separate test
            // to isolate failure modes: Step 1 red + Step 2 red ≠ same root cause as
            // Step 1 green + Step 2 red.
            var chaptersJson = await Subject.ProxyFetchAsync($"/manga/{KnownGoodMangaHid}/chapters");
            var chapterId = ExtractFirstChapterId(chaptersJson);
            chapterId.Should().NotBeNullOrEmpty(
                "Step 2 preflight: cannot exercise the chapter-detail captureToken route " +
                "without a chapter ID from Step 1.");

            var pagesJson = await Subject.ProxyFetchAsync($"/chapters/{chapterId}");

            pagesJson.Should().NotBeNullOrWhiteSpace(
                "Step 2: chapter-detail captureToken must return a non-empty body.");

            pagesJson.Should().Contain("\"pages\"",
                "Step 2: chapter-detail body shape per ComixChapterPagesResponse — " +
                "`result.pages.{baseUrl, items[]}`. If this fails, comix.to may have " +
                "re-shaped its chapter-detail envelope.");
        }

        [OneTimeTearDown]
        public void TearDownLiveSigner()
        {
            Subject?.Dispose();
        }

        // Defensive JSON parse — comix.to's chapter `id` is currently a number per the
        // 2026-05-10 survey, but accept either kind so the helper isn't brittle.
        private static string ExtractFirstChapterId(string json)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("result", out var result) &&
                    result.TryGetProperty("items", out var items) &&
                    items.GetArrayLength() > 0 &&
                    items[0].TryGetProperty("id", out var idEl))
                {
                    return idEl.ValueKind == System.Text.Json.JsonValueKind.String
                        ? idEl.GetString()
                        : idEl.GetRawText();
                }
            }
            catch
            {
                // Caller handles null → null-or-empty assertion failure surfaces the
                // upstream-shape change with the captured body for triage.
            }

            return null;
        }
    }
}
