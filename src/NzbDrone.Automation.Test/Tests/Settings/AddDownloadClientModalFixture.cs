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
/// Phase 20 Plan 20-05 — INVENTORY modal-action row AddDownloadClientModal.
/// Adds an InProcess DownloadClient via the AddDownloadClient picker flow and
/// asserts the new card persists, then deletes it via the flow helper.
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
        await new SettingsDownloadClientsPage(Page).OpenAsync(RootUri);
        await SettingsProviderFlow.OpenPickerAndSelectAsync(Page, "downloadclient", "inprocessimage");

        var modal = new EditDownloadClientModal(Page);
        var name = $"AddTest-{Guid.NewGuid():N}";
        await modal.NameInput.FillAsync(name);

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
