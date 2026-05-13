using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Wanted;

[TestFixture]
[Category("AutomationTest")]
public class MangaMissingFixture : AutomationTest
{
    // Stable MangaDex UUID used by Plan-04 cassettes. AddMangaFlow records the
    // initial-add network exchange to a cassette under Fixtures/Cassettes/MangaDex/
    // so cluster fixtures can seed deterministically.
    private const string KnownMangaDexId = "a96676e5-8ae2-425e-b549-7f15dd34a6d8";

    [Test]
    public async Task missing_page_renders_table_or_empty_state()
    {
        // Seed manga via UI flow (D-06 — UI-populates-via-UI; D-08 first-class
        // AddByMangaDexIdAsync helper). Wave 2 parallel-plan dependency on Plan-04.
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);
        await new MangaMissingPage(Page).OpenAsync(RootUri);

        // Page-level table testid must be visible (the Missing page wraps the
        // table block with `manga-missing-table` from Plan 18-06 Task 1).
        await Assertions.Expect(Page.GetByTestId("manga-missing-table")).ToBeVisibleAsync();

        // STATE assertion (per feedback_verify_ui_state_not_just_rendering): row
        // shells are not enough — every rendered row must expose its chapter cell
        // so we know the row interior is populated, not just the <tr> count.
        var rows = Page.GetByTestId(new System.Text.RegularExpressions.Regex(@"^manga-missing-row-\d+$"));
        var count = await rows.CountAsync();
        for (var i = 0; i < count; i++)
        {
            var row = rows.Nth(i);
            var idAttr = await row.GetAttributeAsync("data-testid");
            var rowId = idAttr!.Replace("manga-missing-row-", string.Empty);
            await Assertions.Expect(Page.GetByTestId($"manga-missing-row-{rowId}-chapter")).ToBeVisibleAsync();
        }

        Page.Url.Should().EndWith("/manga/wanted/missing");
    }
}
