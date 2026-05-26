using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Modals;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// GH #163 (LiveService-exemption tracker resolution) — D-08 paired-offline
/// companion for IndexerTestButtonFixture (INVENTORY v5-endpoint row 104:
/// POST /api/v5/indexer/test). Fills a real D-08 obligation gap: the GH #163
/// issue body and the Phase 20 plan footer both reference this fixture as
/// "Phase 18" coverage, but the file was never authored (verified by `find`
/// across the repo at GH #163 resolution time on 2026-05-16). This fixture
/// closes that gap so the LiveService row's D-08 obligation is genuinely
/// discharged.
///
/// Asserts (seeds/liveservice-coverage-policy.md §3 verbatim):
///   (a) form-render — the Edit modal opens for the seeded MangaDex card
///       and the Test button is visible
///   (b) button-wiring — clicking the Test button reaches the API request
///       layer (the browser-to-Mangarr POST is observable)
///   (c) request-shape — URL + method captured via Page.WaitForRequestAsync
///
/// Symmetry with IndexerTestAllOfflineFixture + IndexerActionButtonOfflineFixture:
/// observes the browser-to-Mangarr surface (NOT the Mangarr-to-upstream hop).
/// The Mangarr-to-MangaDex hop is what the LiveService fixture covers; in
/// offline mode the upstream is unreachable but the wire-level contract that
/// the Test button POSTs to /api/v5/indexer/test is the assertion that
/// matters here. PRSmoke tier — fast (no live upstream wait).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class IndexerTestButtonOfflineFixture : AutomationTest
{
    [OneTimeSetUp]
    public async Task SeedAsync()
    {
        var tk = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
        await tk.SeedIndexerAsync();
    }

    [Test]
    public async Task loads_offline_test_shape()
    {
        var settings = await new SettingsIndexersPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(settings.PageContainer).ToBeVisibleAsync();

        var card = settings.CardByName("MangaDex (test seed)");
        await Assertions.Expect(card).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await card.ClickAsync();

        var modal = new EditIndexerModal(Page);

        // (a) form-render — the Edit modal opens with the Test button visible.
        await Assertions.Expect(modal.ModalRoot).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(modal.TestButton).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // (b)+(c) button-wiring + request-shape. The local POST to
        // /api/v5/indexer/test is observable regardless of whether the
        // Mangarr-to-MangaDex upstream hop is reachable (the LiveService
        // sibling fixture is the only one that asserts on the upstream-
        // response branch). WaitForRequestAsync returns synchronously when
        // the matching outbound request fires; we do not await the response.
        var captureTask = Page.WaitForRequestAsync(
            r => r.Url.Contains("/api/v5/indexer/test") && r.Method == "POST",
            new() { Timeout = 30_000 });

        await modal.TestButton.ClickAsync();
        var capturedRequest = await captureTask;

        capturedRequest.Method.Should().Be("POST");
        capturedRequest.Url.Should().Contain("/api/v5/indexer/test");
    }
}
