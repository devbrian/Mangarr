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
/// Phase 20 Plan 20-04 — Indexer action-button coverage (INVENTORY v5-endpoint
/// row: POST /api/v5/indexer/action/{name}). Plan 20-01 LiveService Enumeration
/// row #3 (GH #165).
///
/// Blocker #4 acceptance path (c) — explicit demotion documented in-test:
/// the D-06 canonical Indexer = MangaDex exposes NO provider-action-bearing
/// field (no captcha refresh, no OAuth re-auth, no third-party probe). The
/// EditIndexerModalContent footer for MangaDex shows only Delete /
/// AdvancedSettingsButton (toggle) / Test / Cancel / Save, and ModalContent
/// itself renders an icon-only Close button. None invokes
/// POST /api/v5/indexer/action/{name}.
///
/// This fixture is a deterministic state assertion (NO Inconclusive — Blocker
/// #4 invariant) documenting that the action surface is not reachable through
/// MangaDex. Plan 20-04 Task 4.5 demotes the INVENTORY action row back to ⬜
/// and files a v1.x follow-up GH issue to revisit once a manga-aware indexer
/// with an action-bearing provider field ships (or once a non-canonical
/// indexer is added to the test surface).
///
/// Filter strategy (debug-30 liveservice-action-invokes 2026-05-16): the
/// original implementation chained `Filter(HasNotText = ...)` which only
/// matches *visible* text content. Two canonical buttons — the modal Close
/// button (Components/Modal/ModalContent.tsx) and the AdvancedSettingsButton
/// (Settings/AdvancedSettingsButton.tsx) — are icon-only and slip through
/// every HasNotText filter, producing a spurious count of 2. The fix
/// enumerates all buttons in the modal scope and excludes them by accessible
/// name (textContent || aria-label || title attribute via getAttribute)
/// against a positive whitelist of canonical names. Any button left over
/// must be a providerAction-typed field button.
///
/// GH #165 re-investigation 2026-05-16 (resolves liveservice-exemption tracker):
/// Per Phase 20 D-09(a) the LiveService eligibility bar is "proven offline-
/// impossible during recording — must be 'it does not work', not 'brittle'."
/// This fixture's assertion is `nonStandardNames.Should().BeEmpty(...)` — a
/// pure structural UI invariant on the seeded MangaDex modal. Opening the
/// EditIndexerModal does NOT fire any outbound MangaDex request (the modal
/// hydrates from the local /api/v5/indexer/{id} body; the schema-fetch is the
/// picker path, not the edit path). The companion endpoint-coverage fixture
/// IndexerActionEndpointApiFixture (GH #169 follow-up) exercises POST
/// /api/v5/indexer/action/{name} at the wire level via the local API — so
/// the action-endpoint family is now covered. The LiveService tag is
/// therefore dropped; this fixture rides the default nightly tier (and is
/// also valid offline via PR-smoke since no upstream is touched).
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

        var card = settings.CardByName("MangaDex (test seed)");
        await Assertions.Expect(card).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await card.ClickAsync();

        var modal = new EditIndexerModal(Page);
        await Assertions.Expect(modal.ModalRoot).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Strict deterministic state assertion (Blocker #4 path (c)): enumerate
        // ALL buttons in the modal scope and bucket each into "canonical" (one
        // of the standard chrome buttons) or "non-standard" (would be a
        // providerAction-typed field button rendered by ProviderFieldFormGroup).
        //
        // Accessible-name surrogate per button = textContent OR aria-label OR
        // title attribute (HTML `title=` on the button or its child span). The
        // original Filter(HasNotText=...) chain matched textContent only, which
        // missed icon-only buttons whose name comes from aria-label/title.
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

        nonStandardNames.Should().BeEmpty(
            "MangaDex (D-06 canonical) ships no providerAction-typed fields; "
            + "the action endpoint surface is unreachable through this canonical pick. "
            + "INVENTORY action row demoted; v1.x follow-up GH issue tracks expansion. "
            + "Unexpected non-canonical buttons: [" + string.Join(", ", nonStandardNames) + "]");
    }
}
