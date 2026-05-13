using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 18 Plan 18-07 — UI-07 round-trip STATE assertion (D-14: at least one Settings
/// round-trip in PRSmoke).
///
/// The fixture toggles the UISettings showRelativeDates checkbox, clicks Save (which
/// is wired to <c>SettingsToolbar</c>'s save button), reloads the page, and asserts
/// the value persisted. Cleanup restores the original value on best-effort.
///
/// showRelativeDates (checkbox in UISettings) was chosen because:
///   1. It is visible by default (NOT gated by showAdvancedSettings).
///   2. UI settings have NO restart-required keys — saving never triggers the
///      RestartRequiredModal that would interfere with reload.
///   3. Toggling a boolean is the simplest round-trip target.
///   4. UI settings persist via /api/v5/config/ui which has no side-effects on the
///      Mangarr core process.
///   5. The underlying <c>&lt;input type="checkbox" name="showRelativeDates"&gt;</c>
///      selector works today without depending on Plan-04's CheckInput wrapper
///      data-testid passthrough.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")] // D-14 — at least one Settings round-trip in PRSmoke
public class SettingsSaveRoundTripFixture : AutomationTest
{
    [Test]
    public async Task ui_settings_save_persists_across_reload()
    {
        var page = await new SettingsUIPage(Page).OpenAsync(RootUri);

        var checkbox = Page.Locator("input[type='checkbox'][name='showRelativeDates']");
        await checkbox.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

        var originalChecked = await checkbox.IsCheckedAsync();
        var targetChecked = !originalChecked;

        // Click the visible CheckInput wrapper — CheckInput renders the native input
        // hidden behind a styled CSS icon; the FormGroup parent is what receives clicks.
        // Locator(input).ClickAsync with Force directly fires onChange on the input.
        await checkbox.ClickAsync(new LocatorClickOptions { Force = true });
        await Page.WaitForTimeoutAsync(500);

        var afterToggle = await checkbox.IsCheckedAsync();
        afterToggle.Should().Be(targetChecked, "the click must toggle the showRelativeDates checkbox before save");

        // Click Save and wait for the network round-trip to complete.
        await page.SaveButton.ClickAsync();
        await Page.WaitForTimeoutAsync(2_500);

        // Reload — re-mount the React tree and re-fetch GET /api/v5/config/ui.
        await Page.ReloadAsync();
        await page.WaitForLoadedAsync();
        var reloadedCheckbox = Page.Locator("input[type='checkbox'][name='showRelativeDates']");
        await reloadedCheckbox.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

        // STATE assertion: value persisted.
        var persistedChecked = await reloadedCheckbox.IsCheckedAsync();
        persistedChecked.Should().Be(targetChecked, "the showRelativeDates setting must persist after page reload (UI-07 round-trip)");

        // Restore (best-effort cleanup so subsequent tests see the original value).
        try
        {
            await reloadedCheckbox.ClickAsync(new LocatorClickOptions { Force = true });
            await Page.WaitForTimeoutAsync(500);
            await page.SaveButton.ClickAsync();
            await Page.WaitForTimeoutAsync(500);
        }
        catch
        {
            // Cleanup is best-effort — the integration test data dir is throwaway.
        }
    }
}
