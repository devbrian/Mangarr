using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Modals;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-04 D-08 paired-offline companion for IndexerActionButtonFixture
/// (seeds/liveservice-coverage-policy.md §3 verbatim).
///
/// Asserts:
///   (a) form-render — the Edit modal opens for the seeded Gateway card
///   (b) button-wiring — clicking the Test button (the available action
///       surface on the sole IIndexer; see IndexerActionButtonFixture
///       Blocker #4 path (c) rationale) wires to POST /api/v5/indexer/...
///   (c) request-shape — URL + method captured via Page.Request
///
/// Because the GatewayIndexer (sole IIndexer) exposes no providerAction-bearing
/// footer button (see IndexerActionButtonFixture.cs comment block),
/// this fixture observes the Test button's POST as the wire-level proxy for
/// the action surface family. The INVENTORY action row remains demoted in
/// Task 4.5; this companion still discharges the D-08 paired-offline obligation
/// for LiveService Enumeration row #3 by proving the Edit-modal action surface
/// renders + wires + emits a deterministic request shape on the local
/// browser-to-Mangarr surface. Phase 39 Plan 39-07: repointed from the retired
/// in-process MangaDex indexer to the GatewayIndexer.
///
/// PRSmoke tier — fast (no live upstream wait).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class IndexerActionButtonOfflineFixture : AutomationTest
{
    [OneTimeSetUp]
    public async Task SeedAsync()
    {
        var tk = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
        await tk.SeedIndexerAsync();
    }

    [Test]
    public async Task loads_offline_action_shape()
    {
        var settings = await new SettingsIndexersPage(Page).OpenAsync(RootUri);

        var card = settings.CardByName("Gateway (test seed)");
        await Assertions.Expect(card).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await card.ClickAsync();

        var modal = new EditIndexerModal(Page);

        // (a) form-render — the Edit modal opens with the Test button visible.
        await Assertions.Expect(modal.ModalRoot).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(modal.TestButton).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // (b)+(c) button-wiring + request-shape — capture the outbound POST shape.
        var captureTask = Page.WaitForRequestAsync(
            r => r.Url.Contains("/api/v5/indexer/") && r.Method == "POST",
            new() { Timeout = 30_000 });

        await modal.TestButton.ClickAsync();
        var capturedRequest = await captureTask;

        capturedRequest.Method.Should().Be("POST");
        capturedRequest.Url.Should().Contain("/api/v5/indexer/");
    }
}
