using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Wanted;

[TestFixture]
[Category("AutomationTest")]
public class MangaCutoffUnmetFixture : AutomationTest
{
    private const string KnownMangaDexId = "a96676e5-8ae2-425e-b549-7f15dd34a6d8";

    [Test]
    public async Task cutoff_unmet_page_renders_table_with_state_cells()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);
        await new MangaCutoffUnmetPage(Page).OpenAsync(RootUri);

        await Assertions.Expect(Page.GetByTestId("manga-cutoff-unmet-table")).ToBeVisibleAsync();

        var rows = Page.GetByTestId(new System.Text.RegularExpressions.Regex(@"^manga-cutoff-unmet-row-\d+$"));
        var count = await rows.CountAsync();
        for (var i = 0; i < count; i++)
        {
            var row = rows.Nth(i);
            var idAttr = await row.GetAttributeAsync("data-testid");
            var rowId = idAttr!.Replace("manga-cutoff-unmet-row-", string.Empty);

            // STATE: every row exposes current-quality + cutoff-quality cells (not
            // just the row shell). Per Plan 18-06 Task 1 the testid names track
            // the plan spec verbatim; manga's TranslationProfile-based cutoff
            // axis lands on the chapter cell (cutoff-quality) and the file/avail
            // state cell (current-quality) — both always-visible columns.
            await Assertions.Expect(Page.GetByTestId($"manga-cutoff-unmet-row-{rowId}-current-quality")).ToBeVisibleAsync();
            await Assertions.Expect(Page.GetByTestId($"manga-cutoff-unmet-row-{rowId}-cutoff-quality")).ToBeVisibleAsync();
        }

        Page.Url.Should().EndWith("/manga/wanted/cutoffunmet");
    }
}
