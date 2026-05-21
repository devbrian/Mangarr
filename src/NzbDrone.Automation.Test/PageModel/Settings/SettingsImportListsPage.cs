using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Settings;

// Phase 20 Plan 20-02 — /settings/importlists PageObject (D-17 fluent return-this).
// Greens INVENTORY route-axis row: `route | /settings/importlists | ImportList settings page renders | 🟢`.
// ImportListSettings.tsx is a v1.1+ placeholder per Phase 20 D-06 (no concrete providers ship).
// testid `settings-importlists-page` added inline to frontend/src/Settings/ImportLists/ImportListSettings.tsx
// in the same commit (Phase 19 D-08 fix-inline-when-contained precedent).
public class SettingsImportListsPage : PageBase
{
    public SettingsImportListsPage(IPage page)
        : base(page)
    {
    }

    public ILocator PageContainer => Page.GetByTestId("settings-importlists-page");

    // Phase 27.1 Plan 27.1-05 Task 7 — toolbar locators for the Sonarr-parity
    // PageToolbarButton pair shipped in Plan 27.1-05 Task 2. TestAllButton
    // must be ASSERTED rendered but NEVER ClickedAsync inside automation —
    // RESEARCH §Pitfall 5 documents the synchronous-loop OOM risk.
    public ILocator TestAllButton => Page.GetByTestId("settings-importlists-test-all-button");
    public ILocator ManageButton  => Page.GetByTestId("settings-importlists-manage-button");

    public async Task<SettingsImportListsPage> OpenAsync(string rootUri)
    {
        await Page.GotoAsync($"{rootUri}/settings/importlists");
        return await WaitForLoadedAsync();
    }

    public async Task<SettingsImportListsPage> WaitForLoadedAsync()
    {
        try
        {
            await PageContainer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            await Page.WaitForURLAsync(new Regex(@"/settings/importlists$"));
        }

        return this;
    }
}
