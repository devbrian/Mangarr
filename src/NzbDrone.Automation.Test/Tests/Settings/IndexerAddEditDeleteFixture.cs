using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel.Modals;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-04 (D-06) — Indexer canonical CRUD round-trip against
/// MangaDex: picker -> Add -> Edit (priority) -> Delete. Exercises
/// POST/PUT/DELETE /api/v5/indexer plus the SettingsProviderFlow orchestrator
/// that Plans 20-05/06 will adopt verbatim.
///
/// Tier (D-04): modal-action axis = Nightly (no [Category("PRSmoke")]).
///
/// Comix is disabled in OneTimeSetUp (Pitfall 10) so the picker schema list
/// rendered during OpenPickerAndSelectAsync does not warm PuppeteerSharp.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class IndexerAddEditDeleteFixture : AutomationTest
{
    private const string TestName = "MangaDex (CRUD test)";

    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).DisableComixIndexerAsync();
    }

    [Test]
    public async Task crud_roundtrip_for_mangadex_indexer()
    {
        await new SettingsIndexersPage(Page).OpenAsync(RootUri);
        await SettingsProviderFlow.OpenPickerAndSelectAsync(Page, "indexer", "mangadex");

        var editModal = new EditIndexerModal(Page);
        await editModal.NameInput.FillAsync(TestName);

        await SettingsProviderFlow.SaveAsync(Page, "indexer");

        // Reopen the saved card, change priority, save again -> PUT.
        var settings = new SettingsIndexersPage(Page);
        var card = settings.CardByName(TestName);
        await Assertions.Expect(card).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await card.ClickAsync();
        var editAgain = new EditIndexerModal(Page);
        await Assertions.Expect(editAgain.ModalRoot).ToBeVisibleAsync();
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
