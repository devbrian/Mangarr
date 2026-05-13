using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace NzbDrone.Automation.Test.PageModel.Settings;

// Phase 18 Plan 18-07 — /settings/profiles PageObject. The Profiles route renders the
// manga-canonical TranslationProfiles editor (Phase 7 D-05 + Phase 15 D-12). No page-
// level Save — per-profile rows save via the EditTranslationProfile modal.
public class SettingsTranslationProfilesPage : PageBase
{
    public SettingsTranslationProfilesPage(IPage page)
        : base(page)
    {
    }

    public ILocator PageContainer => Page.GetByTestId("settings-translation-profiles-page");

    // Per-row locators for D-08 SettingsFlow.SetTranslationProfileOrderAsync.
    public ILocator Row(int profileId)
        => Page.GetByTestId($"settings-translation-profiles-row-{profileId}");

    public ILocator RowName(int profileId)
        => Page.GetByTestId($"settings-translation-profiles-row-{profileId}-name");

    public async Task<SettingsTranslationProfilesPage> OpenAsync(string rootUri)
    {
        await Page.GotoAsync($"{rootUri}/settings/profiles");
        return await WaitForLoadedAsync();
    }

    public async Task<SettingsTranslationProfilesPage> WaitForLoadedAsync()
    {
        try
        {
            await PageContainer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            await Page.WaitForURLAsync(new Regex(@"/settings/profiles$"));
        }

        return this;
    }
}
