using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.Global;

/// <summary>
/// Phase 18 Plan 18-16 Task 2 — Tests/Global/ nav-settings sidebar entry
/// (modal-action row 204 in INVENTORY.md). Settings parent expands 11
/// children (MediaManagement / Profiles / CustomFormats / Indexers /
/// DownloadClients / ImportLists / Connect / Metadata / MetadataSource /
/// Tags / General / Ui) when /settings/* is active.
///
/// State assertion: ToHaveAttributeAsync(href) on parent + .CountAsync()
/// on a representative subset of expected child anchors.
///
/// Cross-process AddManga seed dependency: NONE. Live, no [Explicit].
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class NavSettingsDropdownFixture : AutomationTest
{
    [Test]
    public async Task dropdown_links()
    {
        // Settings parent (PageSidebar.tsx L125-L183) `to` = "/settings".
        // Navigate there so it becomes the active parent and its children mount.
        await Page.GotoAsync($"{RootUri}/settings");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // STATE assertion 1: parent nav-settings anchor.
        var navSettings = Page.GetByTestId("nav-settings");
        await Assertions.Expect(navSettings).ToHaveAttributeAsync("href", new Regex(@"/settings$"));

        // STATE assertion 2: representative child links present. We check the
        // 3 most-load-bearing sub-routes (mediamanagement / profiles /
        // customformats); enumerating all 11 would be brittle if a child is
        // re-ordered in a future plan.
        var mediaMgmtChild = Page.Locator("a[href$='/settings/mediamanagement']");
        var profilesChild = Page.Locator("a[href$='/settings/profiles']");
        var customFormatsChild = Page.Locator("a[href$='/settings/customformats']");

        (await mediaMgmtChild.CountAsync()).Should().BeGreaterOrEqualTo(1);
        (await profilesChild.CountAsync()).Should().BeGreaterOrEqualTo(1);
        (await customFormatsChild.CountAsync()).Should().BeGreaterOrEqualTo(1);
    }
}
