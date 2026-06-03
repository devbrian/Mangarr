using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel.Modals;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-05 / Phase 39 Plan 39-07 — INVENTORY modal-action row
/// AddDownloadClientModal. Adds a Gateway DownloadClient via the AddDownloadClient
/// picker flow and asserts the new card persists, then deletes it via the flow helper.
/// Repointed from the retired in-process client (Plan 39-02) — the `inprocessimage`
/// schema slug no longer exists; GatewayDownloadClient (slug `gateway`) is the sole pick.
///
/// The gateway's backend Test() makes a real outbound HTTP call to the (unreachable)
/// gateway host, so BypassConnectionTestAsync injects ?skipTesting=true on the save POST
/// (ProviderControllerBase.CreateProvider:85); the GatewayDownloadClientSettings validator
/// requires a non-empty ApiKey (Host/Port default to localhost:8080), so ApiKey is filled
/// before save. Mirrors DownloadClientCrudFixture / the Komga/Kavita CRUD fixtures.
///
/// Tier (D-04): modal-action axis = Nightly.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class AddDownloadClientModalFixture : AutomationTest
{
    [Test]
    public async Task add_persists()
    {
        // The gateway Test() makes a live outbound call to the unreachable gateway host;
        // inject ?skipTesting=true on the save POST so the add is deterministic.
        await SettingsProviderFlow.BypassConnectionTestAsync(Page, "downloadclient");

        await new SettingsDownloadClientsPage(Page).OpenAsync(RootUri);
        await SettingsProviderFlow.OpenPickerAndSelectAsync(Page, "downloadclient", "gateway");

        var modal = new EditDownloadClientModal(Page);
        var name = $"AddTest-{Guid.NewGuid():N}";
        await modal.NameInput.FillAsync(name);

        // GatewayDownloadClientSettings validator requires a non-empty ApiKey (settings
        // validation runs even with skipTesting); fill it so the save POST clears validation.
        await modal.ApiKeyInput.FillAsync("test-add-key");

        await SettingsProviderFlow.SaveAsync(Page, "downloadclient");

        var page = new SettingsDownloadClientsPage(Page);
        var card = page.CardByName(name);
        await Assertions.Expect(card).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // State assertion (audit-test-assertions.sh): hit the API directly to
        // verify the newly-added DownloadClient persisted in the backend, not
        // just in the UI render.
        var listResp = await Page.APIRequest.GetAsync($"{RootUri}/api/v5/downloadclient");
        listResp.Status.Should().Be(200);
        var bodyText = await listResp.TextAsync();
        bodyText.Should().Contain(name);

        await SettingsProviderFlow.DeleteByNameAsync(Page, "downloadclient", name);
    }
}
