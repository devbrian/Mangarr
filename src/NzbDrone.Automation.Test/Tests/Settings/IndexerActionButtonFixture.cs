using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Modals;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-04 / Phase 39 Plan 39-07 — Indexer action-button coverage
/// (INVENTORY v5-endpoint row: POST /api/v5/indexer/action/{name}). Plan 20-01
/// LiveService Enumeration row #3 (GH #165).
///
/// **Premise INVERTED in Phase 39 Plan 39-07.** The original fixture asserted the
/// canonical MangaDex pick exposed NO providerAction-bearing field (the action-button
/// surface was unreachable). The retired in-process MangaDex indexer (Plan 39-03) is
/// gone; the surviving sole indexer is the GatewayIndexer, whose `GatewaySources` field
/// IS a providerAction-typed Select (`SelectOptionsProviderAction = "gatewaySources"`).
/// ProviderFieldFormGroup renders a refresh/options BUTTON for such fields, so the
/// gateway's EditIndexerModal DOES expose at least one non-canonical (providerAction)
/// button — the action-button surface IS reachable through the sole indexer. This
/// fixture now asserts that POSITIVE invariant (restores the action-button coverage the
/// MangaDex pick could not provide).
///
/// Button-enumeration strategy: enumerate ALL buttons in the modal scope and bucket each
/// into "canonical" (the standard chrome buttons — Save/Test/Delete/Close/Cancel/Advanced
/// toggle) or "non-standard" (a providerAction-typed field button rendered by
/// ProviderFieldFormGroup). Accessible-name surrogate per button = textContent OR
/// aria-label OR title attribute (icon-only buttons name themselves via aria-label/title).
///
/// Opening the EditIndexerModal does NOT fire any outbound gateway request (the modal
/// hydrates from the local /api/v5/indexer/{id} body). The action ENDPOINT round-trip is
/// covered separately by IndexerActionEndpointApiFixture (wire-level via the local API).
/// PRSmoke tier — no upstream is touched.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class IndexerActionButtonFixture : AutomationTest
{
    private static readonly string[] CanonicalButtonNames = new[]
    {
        "Save",
        "Test",
        "Delete",
        "Close",
        "Cancel",
        "Advanced",       // AdvancedSettingsButton: title attr "ShownClickToHide" / "HiddenClickToShow"
        "click to hide",  // AdvancedSettingsButton title attr (en.json "Shown, click to hide")
        "click to show",  // AdvancedSettingsButton title attr (en.json "Hidden, click to show")
    };

    [OneTimeSetUp]
    public async Task SeedIndexerAsync()
    {
        var tk = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
        await tk.SeedIndexerAsync();
    }

    [Test]
    public async Task action_invokes()
    {
        var settings = await new SettingsIndexersPage(Page).OpenAsync(RootUri);

        var card = settings.CardByName("Gateway (test seed)");
        await Assertions.Expect(card).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await card.ClickAsync();

        var modal = new EditIndexerModal(Page);
        await Assertions.Expect(modal.ModalRoot).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Strict deterministic state assertion (Phase 39 inverted premise):
        // enumerate ALL buttons in the modal scope and bucket each into
        // "canonical" (one of the standard chrome buttons) or "non-standard" (a
        // providerAction-typed field button rendered by ProviderFieldFormGroup for
        // the gateway's GatewaySources Select field). At least one non-standard
        // button MUST be present — that is the gateway's reachable action-button surface.
        //
        // Accessible-name surrogate per button = textContent OR aria-label OR
        // title attribute (HTML `title=` on the button or its child span); icon-only
        // buttons name themselves via aria-label/title rather than textContent.
        var buttons = await modal.ModalRoot.GetByRole(AriaRole.Button).AllAsync();
        var nonStandardNames = new List<string>();
        foreach (var btn in buttons)
        {
            var text = (await btn.TextContentAsync() ?? string.Empty).Trim();
            var ariaLabel = await btn.GetAttributeAsync("aria-label") ?? string.Empty;
            var title = await btn.GetAttributeAsync("title") ?? string.Empty;

            // Some icon buttons (e.g. ModalContent close) put the title on a
            // child span wrapping an SVG. Fall back to inner-HTML title= sniff.
            var innerTitle = string.Empty;
            if (string.IsNullOrEmpty(text) && string.IsNullOrEmpty(ariaLabel) && string.IsNullOrEmpty(title))
            {
                var innerHtml = await btn.InnerHTMLAsync() ?? string.Empty;
                var idx = innerHtml.IndexOf("title=\"", StringComparison.Ordinal);
                if (idx >= 0)
                {
                    var start = idx + "title=\"".Length;
                    var end = innerHtml.IndexOf('"', start);
                    if (end > start)
                    {
                        innerTitle = innerHtml.Substring(start, end - start);
                    }
                }
            }

            var surrogate = string.Join(" | ", new[] { text, ariaLabel, title, innerTitle }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

            var isCanonical = CanonicalButtonNames.Any(name =>
                surrogate.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);

            if (!isCanonical)
            {
                nonStandardNames.Add(string.IsNullOrEmpty(surrogate) ? "<unnamed>" : surrogate);
            }
        }

        nonStandardNames.Should().NotBeEmpty(
            "GatewayIndexer (sole IIndexer) exposes a providerAction-typed field "
            + "(GatewaySources Select with SelectOptionsProviderAction=\"gatewaySources\"), "
            + "so ProviderFieldFormGroup renders at least one non-canonical action button — "
            + "the action-button surface IS reachable through the gateway. "
            + "Canonical chrome buttons seen: [" + string.Join(", ", CanonicalButtonNames) + "]");
    }
}
