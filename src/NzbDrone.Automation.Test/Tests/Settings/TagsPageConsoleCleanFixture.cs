using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 22 Plan 22-05 (F-3 fix) -- TAG-01 zero-browser-console-error gate.
///
/// Pins the SC #1 invariant: when the user navigates to <c>/settings/tags</c>
/// on a fresh page-load, the browser console must contain ZERO error-level
/// messages. Before Plan 22-05, <c>frontend/src/Settings/Tags/Tags.tsx</c>
/// dispatched two Redux thunks (<c>fetchDelayProfiles</c> /
/// <c>fetchImportLists</c>) whose backing V5 controllers do not exist yet,
/// producing two <c>GET /api/v5/{delayprofile,importlist} 404</c> entries
/// + matching Redux-thunk error logs on every mount. The Plan 22-05 surgical
/// fix removed those two dispatches (option-A surgical scope -- see
/// <c>.planning/phases/22-tags-repair-v1-1-inserted-2026-05-17/22-05-GREP-VERIFY.md</c>
/// and DIVERGENCE.md "Phase 22 -- Tags.tsx Redux-dispatch removal" entry);
/// this fixture pins the resulting zero-console-error state.
///
/// Console listener subscription happens AFTER the AutomationTest base-class
/// OneTimeSetUp has navigated to <c>RootUri</c> (app-shell load) so any
/// errors fired during the initial homepage hydration are out of scope --
/// this fixture only captures errors that fire during the explicit
/// <c>/settings/tags</c> navigation triggered inside the test body. The
/// captured list is asserted empty after <c>NetworkIdle</c> settle so React
/// Query / SignalR / sibling Settings hooks have all completed their first
/// fetch.
///
/// Categories: AutomationTest (gates Phase 22 close-out validation) +
/// PRSmoke (per 22-PATTERNS.md lines 147..202 -- this fixture sits in the
/// PR-gate tier alongside TagListFixture / TagDetailPanelFixture).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class TagsPageConsoleCleanFixture : AutomationTest
{
    [Test]
    public async Task no_console_errors_on_tags_page_load()
    {
        var consoleErrors = new List<string>();

        // Subscribe BEFORE the /settings/tags navigation -- captures any
        // error-level console events that fire during the page load itself.
        // Errors fired during the base-class homepage hydration (already
        // navigated in OneTimeSetUp) are out of scope for this assertion.
        Page.Console += (_, msg) =>
        {
            if (msg.Type == "error")
            {
                consoleErrors.Add(msg.Text);
            }
        };

        var page = await new SettingsTagsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        // Settle to NetworkIdle so React Query (useTags / useTagDetails /
        // useReleaseProfiles / useConnections / useIndexers) + SignalR
        // resource broadcasts + the retained fetchDownloadClients dispatch
        // have all completed their first fetch and any error-paths have
        // fired. If we asserted immediately after PageContainer-visible we
        // could race a still-pending error.
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        consoleErrors.Should().BeEmpty(
            "Phase 22 SC #1 requires zero browser-console errors on /settings/tags");
    }
}
