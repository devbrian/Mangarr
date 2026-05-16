using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel.Modals;

namespace NzbDrone.Automation.Test.Tests.InteractiveSearch;

/// <summary>
/// Phase 20 Plan 20-10 (Wave 3 InteractiveSearch sweep) — v5-endpoint axis
/// `POST /api/v5/manga/release` (INVENTORY row 100: InteractiveSearch Grab
/// via Release endpoint).
///
/// Tier (D-04): Nightly default — v5-endpoint write-path (POST grab) per
/// the modal-action-leaning destructive contract.
///
/// Chained-flow fixture: AddMangaFlow seed → InteractiveSearch open →
/// GrabAsync(0) which POSTs /api/v5/manga/release and asserts the POST
/// returns 2xx. Then asserts a history row materializes after SignalR push.
///
/// Distinct from InteractiveSearchGrabFixture (PRSmoke, Phase 18 Plan 18-15)
/// which is the canonical end-to-end happy path. This fixture is pinned to
/// the v5-endpoint row's `release_grab_writes_history` covering-test to
/// satisfy reconcile-inventory.py's row-pinning contract.
///
/// State assertion (per feedback_verify_ui_state_not_just_rendering):
///   1. POST /api/v5/manga/release fires (the grab POST awaited inside
///      InteractiveSearchModal.GrabAsync).
///   2. A history row materializes after the grab — proving the full open
///      → search → grab → history pipeline end-to-end.
///
/// Pitfall 10: Comix disabled in OneTimeSetUp.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class ReleaseGrabFixture : AutomationTest
{
    private const string KnownMangaDexId = AddMangaFlow.KnownMangaDexId;

    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty)
            .DisableComixIndexerAsync();
    }

    [Test]
    public async Task release_grab_writes_history()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);

        var slug = Page.Url.Split('/')[^1];
        slug.Should().NotBeNullOrEmpty();

        var modal = await new InteractiveSearchModal(Page).OpenForMangaAsync(RootUri, slug);

        // STATE assertion (precondition): rows render so the grab has something
        // to act on.
        var count = await modal.GetReleaseCountAsync();
        count.Should().BeGreaterThan(
            0,
            "InteractiveSearch must render at least one release row before the grab");

        // Grab — internally awaits the POST /api/v5/manga/release response.
        await modal.GrabAsync(0);

        // STATE assertion: a history row materializes after the grab (SignalR
        // push pipeline). The manga-history-row-{id} testid is the canonical
        // Activity History row identity.
        await Page.GotoAsync($"{RootUri}/manga/activity/history");

        var historyRows = Page.GetByTestId(new Regex(@"^manga-history-row-\d+$"));
        await Assertions.Expect(historyRows.First).ToBeVisibleAsync(new()
        {
            Timeout = 30_000
        });
        var historyCount = await historyRows.CountAsync();
        historyCount.Should().BeGreaterThan(
            0,
            "release grab POST must produce at least one history row via SignalR push");
    }
}
