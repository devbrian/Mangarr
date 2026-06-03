using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel.Modals;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-04 (D-06) / Phase 39 Plan 39-07 — Indexer canonical CRUD round-trip
/// against the GatewayIndexer (the sole IIndexer): picker -> Add -> Edit (priority) ->
/// Delete. Exercises POST/PUT/DELETE /api/v5/indexer plus the SettingsProviderFlow
/// orchestrator that Plans 20-05/06 adopt verbatim.
///
/// Tier (D-04): modal-action axis = Nightly (no [Category("PRSmoke")]).
///
/// Phase 39 Plan 39-07: repointed from the retired in-process MangaDex indexer (deleted
/// in Plan 39-03) to the gateway. The gateway's backend Test() makes a real outbound HTTP
/// call to the (unreachable) gateway host, so BypassConnectionTestAsync injects
/// ?skipTesting=true on every save POST/PUT (ProviderControllerBase.CreateProvider:85) —
/// mirrors the Komga/Kavita CRUD fixtures.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class IndexerAddEditDeleteFixture : AutomationTest
{
    private const string TestName = "Gateway (CRUD test)";

    [Test]
    public async Task crud_roundtrip_for_gateway_indexer()
    {
        // The gateway Test() makes a live outbound call to the unreachable gateway host;
        // inject ?skipTesting=true on every save POST/PUT so the round-trip is deterministic.
        await SettingsProviderFlow.BypassConnectionTestAsync(Page, "indexer");

        await new SettingsIndexersPage(Page).OpenAsync(RootUri);
        await SettingsProviderFlow.OpenPickerAndSelectAsync(Page, "indexer", "gateway");

        var editModal = new EditIndexerModal(Page);
        await editModal.NameInput.FillAsync(TestName);

        // GatewaySettings validator requires BaseUrl (ValidRootUrl) + ApiKey (NotEmpty);
        // the picker schema preset leaves both empty, so the save POST would 400 on
        // settings validation (which runs even with skipTesting) without these.
        await editModal.BaseUrlInput.FillAsync("http://localhost:8080");
        await editModal.ApiKeyInput.FillAsync("test-crud-key");

        await SettingsProviderFlow.SaveAsync(Page, "indexer");

        // Reopen the saved card, change priority, save again -> PUT.
        var settings = new SettingsIndexersPage(Page);
        var card = settings.CardByName(TestName);
        await Assertions.Expect(card).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await card.ClickAsync();
        var editAgain = new EditIndexerModal(Page);
        await Assertions.Expect(editAgain.ModalRoot).ToBeVisibleAsync();

        // debug-30 iter-2 (2026-05-16): Priority lives inside an
        // isAdvanced={true} FormGroup that doesn't render unless
        // AdvancedSettings is toggled on (advancedSettingsStore persisted in
        // localStorage). Toggle before filling.
        await editAgain.AdvancedToggle.ClickAsync();
        await Assertions.Expect(editAgain.PriorityInput).ToBeVisibleAsync(new() { Timeout = 5_000 });
        await editAgain.PriorityInput.FillAsync("30");

        var putTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/indexer/") && r.Request.Method == "PUT",
            new() { Timeout = 30_000 });
        await editAgain.SaveButton.ClickAsync();
        var putResp = await putTask;
        putResp.Status.Should().BeInRange(200, 299);
        await Assertions.Expect(editAgain.ModalRoot).ToBeHiddenAsync(new() { Timeout = 15_000 });

        // Delete via the flow helper.
        await SettingsProviderFlow.DeleteByNameAsync(Page, "indexer", TestName);
    }
}
