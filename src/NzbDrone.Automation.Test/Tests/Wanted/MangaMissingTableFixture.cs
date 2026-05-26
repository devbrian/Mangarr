using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Wanted;

/// <summary>
/// Phase 20 Plan 20-09 (Wave 3 Wanted modal sweep) — Missing table-load
/// coverage (INVENTORY v5-endpoint row 97: GET /api/v5/manga/wanted/missing).
///
/// Tier (D-04): PRSmoke per the axis-based heuristic — `v5-endpoint` GET-heavy
/// row.
///
/// Seeds a manga via AddMangaFlow (the populated path is not guaranteed since
/// AddMangaFlow's MonitorType default may leave chapters un-monitored or with
/// files). The fixture asserts the V5 endpoint round-trips 2xx with a shape
/// the React `useMissing` hook can consume — either the populated table
/// renders OR the empty-state Alert renders (both are valid table-load
/// outcomes; the v5-endpoint contract is the round-trip itself).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class MangaMissingTableFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [Test]
    public async Task table_loads()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        // Arm response listener BEFORE navigation so we capture the first
        // GET /api/v5/manga/wanted/missing request.
        var wantedTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/manga/wanted/missing") && r.Request.Method == "GET",
            new() { Timeout = 30_000 });

        await new MangaMissingPage(Page).OpenAsync(RootUri);
        var resp = await wantedTask;

        // STATE assertion 1: v5-endpoint responds 2xx.
        resp.Status.Should().BeInRange(
            200,
            299,
            "GET /api/v5/manga/wanted/missing must return 2xx");

        // STATE assertion 2: page shell mounts (Missing.tsx wraps both
        // populated AND empty-state branches inside `manga-missing-page`).
        await Assertions.Expect(Page.GetByTestId("manga-missing-page")).ToBeVisibleAsync();

        // STATE assertion 3: the response is a PagingResource envelope — body
        // must contain `records` (the canonical PagingResource<ChapterResource>
        // wire shape). Both empty and populated branches satisfy this
        // structural assertion.
        var body = await resp.TextAsync();
        body.Should().Contain(
            "records",
            "GET /api/v5/manga/wanted/missing must return a PagingResource envelope");

        Page.Url.Should().EndWith("/manga/wanted/missing");
    }
}
