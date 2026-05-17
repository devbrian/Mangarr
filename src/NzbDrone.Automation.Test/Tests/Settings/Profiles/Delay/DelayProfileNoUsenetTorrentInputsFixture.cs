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

        // Open the Add Delay Profile modal by evaluating page JS that
        // clicks the add icon-button inside the fieldset whose legend
        // reads "Delay Profiles". This avoids fragile cross-version
        // Locator filter API differences (Microsoft.Playwright 1.59
        // exposes `LocatorFilterOptions.Has` but the pattern is not
        // exercised elsewhere in this test suite, so we keep to the
        // established DOM-query idiom by using `EvaluateAsync` to walk
        // the legend -> parent fieldset -> first .plus svg's closest
        // clickable ancestor). The add control is a Link/Button
        // rendered via `<Icon name={icons.ADD}>` which FontAwesome
        // renders as `<svg data-icon='plus'>` per the
        // CustomFormatExportImportFixture.cs:106 comment.
        await Page.EvaluateAsync(@"
            (() => {
                const legends = Array.from(document.querySelectorAll('legend'));
                const target = legends.find(l => l.textContent && l.textContent.trim() === 'Delay Profiles');
                if (!target) throw new Error('Delay Profiles legend not found');
                const fieldset = target.closest('fieldset');
                if (!fieldset) throw new Error('No parent fieldset for Delay Profiles legend');
                const plus = fieldset.querySelector('svg[data-icon=""plus""]');
                if (!plus) throw new Error('No plus svg inside Delay Profiles fieldset');
                const clickable = plus.closest('a, button');
                if (!clickable) throw new Error('No clickable ancestor for the plus svg');
                clickable.click();
            })();
        ");

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
