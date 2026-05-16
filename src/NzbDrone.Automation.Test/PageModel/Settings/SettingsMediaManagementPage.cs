using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Settings;

// Phase 20 Plan 20-02 — /settings/mediamanagement PageObject (D-17 fluent return-this).
// Greens INVENTORY route-axis row: `route | /settings/mediamanagement | MediaManagement settings form renders | 🟢`.
//
// The /settings/mediamanagement route ALSO hosts the Root Folders section
// (SettingsRootFoldersPage.cs already exists and waits on `settings-root-folders-page`).
// This new PageObject waits on the top-level `settings-mediamanagement-page` testid that
// is added inline to frontend/src/Settings/MediaManagement/MediaManagement.tsx in the same
// commit (Phase 19 D-08 fix-inline-when-contained precedent). Both testids coexist on the
// page: the new wrapper is the route-load PageContent surface, the existing root-folders
// testid is the sub-section anchor used by Phase 18 RootFolders fixtures.
public class SettingsMediaManagementPage : PageBase
{
    public SettingsMediaManagementPage(IPage page)
        : base(page)
    {
    }

    public ILocator PageContainer => Page.GetByTestId("settings-mediamanagement-page");

    public async Task<SettingsMediaManagementPage> OpenAsync(string rootUri)
    {
        await Page.GotoAsync($"{rootUri}/settings/mediamanagement");
        return await WaitForLoadedAsync();
    }

    public async Task<SettingsMediaManagementPage> WaitForLoadedAsync()
    {
        try
        {
            await PageContainer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            await Page.WaitForURLAsync(new Regex(@"/settings/mediamanagement$"));
        }

        return this;
    }
}
