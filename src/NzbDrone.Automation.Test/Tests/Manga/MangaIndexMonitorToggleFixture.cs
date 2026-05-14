using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;

namespace NzbDrone.Automation.Test.Tests.Manga;

// Phase 18 Plan-15 gap closure — req PIPELINE-05 (Manga-level monitor toggle).
// INVENTORY line 59 (first clause: "User can monitor/unmonitor at Manga ... level").
//
// The Mangarr poster card has no per-card monitor toggle (Posters view shows only
// Refresh/Search/Edit; toggle lives in the MangaStatusCell isSelectMode path).
// Canonical Manga-level monitor toggle for a single fixture lives on MangaDetails
// (always visible) — AddMangaFlow.AddByMangaDexIdAsync lands directly on
// /manga/{slug}, so this fixture exercises the toggle at the page where it ships
// always-visible. The toggle dispatches PUT /api/v5/manga/{id} via
// useToggleMangaMonitored.
//
// [Explicit] cite: AddMangaFlow.AddByMangaDexIdAsync currently times out at
// AddMangaModal.ConfirmAddAsync due to the WaitForURLAsync race documented in
// Plan 18-14 D-D (issue #102). Removes when D-D ships.
[TestFixture]
[Category("AutomationTest")]
public class MangaIndexMonitorToggleFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [Test]
    public async Task toggle_persists()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        // AddMangaFlow lands on /manga/{slug} — the MonitorToggleButton on
        // MangaDetails is the canonical Manga-level monitor toggle surface.
        var toggle = Page.GetByTestId("manga-details-monitor-toggle");
        await Assertions.Expect(toggle).ToBeVisibleAsync();

        // Read initial state via the aria-label (MonitorToggleButton sets aria-label
        // to translate('ToggleMonitoredToUnmonitored') when monitored, or the
        // 'ToggleUnmonitoredToMonitored' string when unmonitored). The label is the
        // contract by which screen readers + this fixture detect state.
        var beforeLabel = await toggle.GetAttributeAsync("aria-label");
        beforeLabel.Should().NotBeNull();

        // Click to flip the monitored state. Then wait for the PUT
        // /api/v5/manga/{id} round-trip to settle.
        await toggle.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var afterLabel = await toggle.GetAttributeAsync("aria-label");
        afterLabel.Should().NotBeNull();

        // STATE assertion (per feedback_verify_ui_state_not_just_rendering):
        // the toggle's aria-label MUST have flipped between the two
        // translate('ToggleMonitoredToUnmonitored') / 'ToggleUnmonitoredToMonitored'
        // strings. A no-op click (silent backend rejection) would leave the label
        // unchanged and this assertion would fail.
        afterLabel.Should().NotBe(beforeLabel);
    }
}
