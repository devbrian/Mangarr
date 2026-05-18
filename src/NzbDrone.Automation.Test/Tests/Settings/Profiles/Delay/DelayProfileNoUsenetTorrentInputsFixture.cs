using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.Settings.Profiles.Delay;

/// <summary>
/// Phase 23 Plan 23-03 (DP-05) — state-not-rendering assertion that the
/// DelayProfile edit modal does NOT render the legacy Usenet/Torrent
/// delay inputs after the Plan 23-03 frontend trim landed.
///
/// Per <c>feedback_verify_ui_state_not_just_rendering</c>: this fixture
/// asserts the SPECIFIC inputs are ABSENT from the DOM (locator count
/// = 0), not merely "row count = N". A passing test guarantees the
/// 4 user-locked dead fields cannot regress into the user-facing
/// surface without flipping this fixture red.
///
/// URL discrepancy correction from Plan 23-01 SUMMARY decisions[3]:
/// the navigable Settings route at HEAD 42adc910a is <c>/settings/profiles</c>,
/// NOT <c>/settings/profiles/delay</c>. The DelayProfiles fieldset
/// renders inline inside the Profiles parent page
/// (frontend/src/Settings/Profiles/Profiles.tsx:46 mounts DelayProfiles
/// within DndProvider alongside TranslationProfiles + ReleaseProfiles).
///
/// Positive control: the HttpDelay input MUST be present — proves the
/// modal is actually open and rendered (not "everything is absent
/// because the modal never opened").
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class DelayProfileNoUsenetTorrentInputsFixture : AutomationTest
{
    [Test]
    public async Task usenet_torrent_inputs_absent()
    {
        await Page.GotoAsync($"{RootUri}/settings/profiles");

        // Wait until the DelayProfiles fieldset has rendered. The
        // fieldset legend uses the DelayProfiles translation key
        // ("Delay Profiles" in en.json). Pattern carried from
        // CustomFormatExportImportFixture.cs:100 +
        // NotificationTestButtonFixture.cs:140.
        var delayLegend = Page.Locator("legend").GetByText("Delay Profiles");
        try
        {
            await delayLegend.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            await Page.WaitForURLAsync(new Regex(@"/settings/profiles$"));
        }

        var delayLegendCount = await delayLegend.CountAsync();
        delayLegendCount.Should().BeGreaterThan(
            0,
            "Settings/Profiles must expose the Delay Profiles FieldSet section for DP-03 / DP-05");

        // Open the Add Delay Profile modal via the canonical
        // `data-testid` selector (Phase 18 D-18 + CodeRabbit PR #198
        // finding 3255666020). `DelayProfiles.tsx` annotates the Add
        // Link with `data-testid="settings-add-delay-profile"`.
        await Page.GetByTestId("settings-add-delay-profile").ClickAsync();

        // Wait for the modal to mount. The modal body renders a form
        // with the httpDelay input — use that as the readiness signal
        // AND as the positive-control assertion in a single step.
        var httpDelayInput = Page.Locator("[name='httpDelay']");
        await Assertions.Expect(httpDelayInput).ToHaveCountAsync(1);

        // State-not-rendering assertions per
        // feedback_verify_ui_state_not_just_rendering: assert SPECIFIC
        // inputs ABSENT from the DOM, NOT just a row count.
        var usenetDelayInput = Page.Locator("[name='usenetDelay']");
        await Assertions.Expect(usenetDelayInput).ToHaveCountAsync(0);

        var torrentDelayInput = Page.Locator("[name='torrentDelay']");
        await Assertions.Expect(torrentDelayInput).ToHaveCountAsync(0);

        // The protocol dropdown was hidden per A5 resolution (b) —
        // verify it also does NOT render. Defense-in-depth: a
        // regression that re-introduces the dropdown should flip
        // this fixture red even as a single-option select.
        var protocolDropdown = Page.Locator("[name='protocol']");
        await Assertions.Expect(protocolDropdown).ToHaveCountAsync(0);
    }
}
