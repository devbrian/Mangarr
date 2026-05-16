using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Settings;

// Phase 18 Plan 18-07 — /settings/downloadclients PageObject.
public class SettingsDownloadClientsPage : PageBase
{
    public SettingsDownloadClientsPage(IPage page)
        : base(page)
    {
    }

    public ILocator PageContainer => Page.GetByTestId("settings-download-clients-page");
    public ILocator SaveButton    => Page.GetByTestId("settings-save-button");
    public ILocator TestAllButton => Page.GetByTestId("settings-download-clients-test-all-button");
    public ILocator ManageButton  => Page.GetByTestId("settings-download-clients-manage-button");

    // Phase 20 Plan 20-05 — D-18 selector for a download client card. Mirrors the
    // SettingsIndexersPage.CardByName shape from Phase 18 / Plan 20-04: the testid
    // lands on the Card-underlay <button> (Card.tsx overlay branch) via the
    // settings-downloadclient-card-{slug} testid added to DownloadClient.tsx in
    // Plan 20-05. Slug = lowercase name with whitespace collapsed to dashes.
    public ILocator CardByName(string name)
        => Page.GetByTestId($"settings-downloadclient-card-{Slugify(name)}");

    private static string Slugify(string name)
        => Regex.Replace(name.ToLowerInvariant(), @"\s+", "-");

    public async Task<SettingsDownloadClientsPage> OpenAsync(string rootUri)
    {
        await Page.GotoAsync($"{rootUri}/settings/downloadclients");
        return await WaitForLoadedAsync();
    }

    public async Task<SettingsDownloadClientsPage> WaitForLoadedAsync()
    {
        try
        {
            await PageContainer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            await Page.WaitForURLAsync(new Regex(@"/settings/downloadclients$"));
        }

        return this;
    }
}
