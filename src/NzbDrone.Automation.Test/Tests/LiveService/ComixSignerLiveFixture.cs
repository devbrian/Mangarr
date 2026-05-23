using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers.Comix;
using NzbDrone.Test.Common;

namespace NzbDrone.Automation.Test.Tests.LiveService;

/// <summary>
/// Phase 18 D-10 LiveService tier — Phase 17.2's <c>ComixSignerLiveFixture</c>
/// pattern (originally <c>src/Mangarr.Comix.Live.Test/ComixSignerLiveFixture.cs</c>)
/// ported into Phase 18's nightly-only test tier. Excluded from PR CI smoke via
/// the test-category filter <c>TestCategory=AutomationTest&amp;TestCategory!=LiveService</c>
/// (D-14); included in Plan-10's <c>automation_test_liveservice</c> nightly workflow,
/// which selects <c>TestCategory=AutomationTest&amp;TestCategory=LiveService</c>
/// (see <c>.github/workflows/build_v5.yml</c>). Both categories are load-bearing —
/// dropping either silently removes the fixture from nightly coverage.
///
/// Closes GH #101. Per the issue's 2026-05-23 update, **Option A** chosen:
/// inherit <c>TestBase&lt;ComixPuppeteerSigner&gt;</c> from
/// <c>NzbDrone.Test.Common</c> (AutoMoq-resolved signer subject) instead of
/// <c>AutomationTest</c> (which boots the Mangarr backend via NzbDroneRunner).
/// LiveService probes for the signer don't need the backend — they exercise the
/// PuppeteerSharp child process + decoded-body contract directly. This mirrors
/// the Phase 17.2 sibling fixture verbatim so the two stay in sync across signer
/// rotations.
///
/// T-18-04 mitigation (DoS/rate-limit posture): nightly cadence; honest
/// User-Agent <c>"Mangarr-CI/1.0 (https://github.com/devbrian/Mangarr; LiveService
/// nightly contract probe)"</c>; single known-good comix slug; never crawls.
///
/// Cross-reference: <c>.planning/phases/18-.../INVENTORY.md</c> LiveService row
/// for <c>COMIX-SIGNER-01</c>.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("LiveService")]
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

    [Test]
    public async Task ProxyFetchKeywordSearch_returns_decoded_JSON_with_items_array()
    {
        // 2026-05-23 cascade fix (PR #244): the title→hid keyword-search endpoint
        // /api/v1/manga?keyword=... now routes through the signer too. The bundle's
        // pre-installed ok+result interceptor strips the envelope so items sit at the
        // unwrapped JSON root.
        var json = await Subject.ProxyFetchAsync("/manga?keyword=The%20Forgotten%20Field&limit=10");
        json.Should().NotBeNullOrWhiteSpace();
        json.Should().Contain("\"items\"",
            "the env-module oracle returns the unwrapped result shape: " +
            "{items:[...], meta:...}");
    }

    [OneTimeTearDown]
    public void TearDownLiveSigner()
    {
        Subject?.Dispose();
    }

    private static string ExtractFirstChapterId(string json)
    {
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // PR #244 Investigation Phase 3 (2026-05-23) oracle pivot: the
        // env-module-oracle returns the unwrapped `result` object directly (the
        // bundle's ok+result unwrap interceptor strips the
        // `{status:"ok", result:{...}}` envelope). Older Phase 17.2 shape kept the
        // envelope intact; accept both for forward compatibility across rotations.
        if (root.TryGetProperty("items", out var topItems))
        {
            if (topItems.GetArrayLength() > 0
                && topItems[0].TryGetProperty("id", out var idEl1))
            {
                return idEl1.ValueKind == JsonValueKind.String
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
            return idEl2.ValueKind == JsonValueKind.String
                ? idEl2.GetString()
                : idEl2.GetRawText();
        }

        return null;
    }
}
