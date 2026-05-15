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
/// AdvancedSettingsButton (toggle) / Test / Cancel / Save — none of which
/// invokes POST /api/v5/indexer/action/{name}.
///
/// This fixture is a deterministic state assertion (NO Inconclusive — Blocker
/// #4 invariant) documenting that the action surface is not reachable through
/// MangaDex. Plan 20-04 Task 4.5 demotes the INVENTORY action row back to ⬜
/// and files a v1.x follow-up GH issue to revisit once a manga-aware indexer
/// with an action-bearing provider field ships (or once a non-canonical
/// indexer is added to the test surface).
///
/// LiveService tag retained for catalog continuity (Plan 20-01 enumeration row
/// #3 stays in the catalog; status flips to `blocked` with cross-reference to
/// this fixture's documenting role — see SUMMARY).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("LiveService")]
public class IndexerActionButtonFixture : AutomationTest
{
    [OneTimeSetUp]
    public async Task SeedIndexerAsync()
    {
        var tk = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
        await tk.DisableComixIndexerAsync();
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
        // all buttons in the modal footer and confirm NONE is a provider-action
        // button (action buttons would surface as buttons whose role is not in
        // {Save, Test, Delete, Cancel, Advanced} — typically rendered by
        // ProviderFieldFormGroup for fields of type providerAction).
        var nonStandardButtons = modal.ModalRoot.GetByRole(AriaRole.Button)
            .Filter(new() { HasNotText = "Save" })
            .Filter(new() { HasNotText = "Test" })
            .Filter(new() { HasNotText = "Delete" })
            .Filter(new() { HasNotText = "Close" })
            .Filter(new() { HasNotText = "Cancel" })
            .Filter(new() { HasNotText = "Advanced" });
        var actionButtonCount = await nonStandardButtons.CountAsync();

        actionButtonCount.Should().Be(
            0,
            "MangaDex (D-06 canonical) ships no providerAction-typed fields; "
            + "the action endpoint surface is unreachable through this canonical pick. "
            + "INVENTORY action row demoted; v1.x follow-up GH issue tracks expansion.");
    }
}
