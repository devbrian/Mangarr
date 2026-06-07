using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Wanted;

/// <summary>
/// Phase 20 Plan 20-09 (Wave 3 Wanted modal sweep) — CutoffUnmet table-load
/// coverage (INVENTORY v5-endpoint row 98: GET /api/v5/manga/wanted/cutoff).
///
/// Tier (D-04): PRSmoke per the axis-based heuristic — `v5-endpoint` GET-heavy
/// row.
///
/// Seeds a manga via AddMangaFlow. AddMangaFlow's defaults are insufficient
/// to produce a populated CutoffUnmet table (per Phase 19 Plan 19-05 gh #153
/// — see MangaCutoffUnmetFixture for the populated-state seed pattern using
/// `SeedCutoffUnmetChapterAsync`), but the v5-endpoint table-load row
/// covers the round-trip contract specifically (NOT row content state).
/// Both empty-state Alert AND populated branch satisfy this row.
///
/// Distinct from MangaCutoffUnmetFixture (Phase 18 Plan-06 — covers the
/// populated path + row-state cells via SeedCutoffUnmetChapterAsync).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class MangaCutoffTableFixture : AutomationTest
{
    private const string KnownMangaBakaId = AddMangaFlow.KnownMangaBakaId;

    [Test]
    public async Task table_loads()
    {
        await AddMangaFlow.AddByMangaBakaIdAsync(Page, RootUri, KnownMangaBakaId);

        var cutoffTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/manga/wanted/cutoff") && r.Request.Method == "GET",
            new() { Timeout = 30_000 });

        await new MangaCutoffUnmetPage(Page).OpenAsync(RootUri);
        var resp = await cutoffTask;

        // STATE assertion 1: v5-endpoint responds 2xx.
        resp.Status.Should().BeInRange(
            200,
            299,
            "GET /api/v5/manga/wanted/cutoff must return 2xx");

        // STATE assertion 2: page shell mounts (CutoffUnmet.tsx wraps both
        // populated AND empty-state branches inside `manga-cutoff-unmet-page`).
        await Assertions.Expect(Page.GetByTestId("manga-cutoff-unmet-page")).ToBeVisibleAsync();

        // STATE assertion 3: PagingResource envelope shape.
        var body = await resp.TextAsync();
        body.Should().Contain(
            "records",
            "GET /api/v5/manga/wanted/cutoff must return a PagingResource envelope");

        Page.Url.Should().EndWith("/manga/wanted/cutoffunmet");
    }
}
