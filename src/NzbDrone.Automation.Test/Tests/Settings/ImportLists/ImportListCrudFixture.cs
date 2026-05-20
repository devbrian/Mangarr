using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings.ImportLists;

// Phase 26 Plan 26-06 Task 1 (D-10 bucket B) — CRUD round-trip on the V5
// ImportList provider surface (/api/v5/importlist).
//
// Phase 27 retarget (closes GH #217): originally registered TestImportList
// (NzbDrone.Core.Test fake) which the production DI scan excludes per Pitfall 2.
// Now uses MangaDexImportList (a real Phase 27 provider) via
// TestKit.RegisterMangaDexImportListAsync(), exercising the V5 controller
// POST → GET → PUT → DELETE round-trip end-to-end with no Assert.Inconclusive
// branch. Dummy credentials are used (skipTesting=true bypasses Settings
// validation; the CRUD flow does NOT require working OAuth).
//
// Analog: src/NzbDrone.Automation.Test/Tests/Settings/IndexerAddEditDeleteFixture.cs
// (provider CRUD pattern — picker → Save → Edit → Save → Delete).
//
// Pattern κ: zero series-*/episode-*/season-*/add-series- selectors. Uses
// `settings-importlist-*` + `add-importlist-*` + `edit-importlist-*` allowed
// prefix family per Phase 18 D-18.
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class ImportListCrudFixture : AutomationTest
{
    private const string TestName = "MangaDex (CRUD test)";

    [OneTimeSetUp]
    public async Task DisableComixAndRegisterProviderAsync()
    {
        var tk = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
        await tk.DisableComixIndexerAsync();

        // Pre-flight registration check. The OneTimeSetUp tolerates failure
        // here (the actual registration happens in [Test] body so individual
        // tests can surface their own diagnostic on miss).
        var (_, _) = await tk.RegisterMangaDexImportListAsync(TestName);
    }

    [Test]
    public async Task crud_round_trip_with_TestImportList()
    {
        var tk = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
        var (registrationResp, definitionId) = await tk.RegisterMangaDexImportListAsync(TestName);

        definitionId.Should().NotBeNull(
            "POST /api/v5/importlist (MangaDexImportList) should succeed in Phase 27+ " +
            "(real provider registered in production DI scan). Response body: {0}",
            registrationResp.Content);

        // 1. GET — verify the V5 controller returns the row we just POST'd.
        var settings = await new SettingsImportListsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(settings.PageContainer).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // The card slug is the lower-kebab of the definition Name per
        // ImportList.tsx (settings-importlist-card-{testIdSlug}).
        var cardSlug = Regex.Replace(TestName.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        var card = Page.GetByTestId($"settings-importlist-card-{cardSlug}");
        await Assertions.Expect(card).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // 2. PUT (Edit) — open the EditImportListModal, mutate the Name, save.
        await card.ClickAsync();

        var editModal = Page.GetByTestId("edit-importlist-modal");
        await Assertions.Expect(editModal).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        var nameInput = editModal.GetByTestId("settings-importlist-field-name");
        const string MutatedName = "MangaDex (CRUD test renamed)";
        await nameInput.FillAsync(MutatedName);

        var putTask = Page.WaitForResponseAsync(
            r => r.Url.Contains($"/api/v5/importlist/{definitionId}") && r.Request.Method == "PUT",
            new PageWaitForResponseOptions { Timeout = 30_000 });
        await editModal.GetByTestId("save-button").ClickAsync();
        var putResp = await putTask;
        putResp.Status.Should().BeInRange(
            200,
            299,
            "PUT /api/v5/importlist/{0} should round-trip the rename",
            definitionId);
        await Assertions.Expect(editModal).ToBeHiddenAsync(
            new LocatorAssertionsToBeHiddenOptions { Timeout = 15_000 });

        // 3. State assertion (not just rendering — per
        //    `feedback_verify_ui_state_not_just_rendering`): the card on the page
        //    now carries the mutated slug.
        var renamedSlug = Regex.Replace(MutatedName.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        var renamedCard = Page.GetByTestId($"settings-importlist-card-{renamedSlug}");
        await Assertions.Expect(renamedCard).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // 4. DELETE — open the renamed card, click Delete, confirm.
        await renamedCard.ClickAsync();
        var editAgain = Page.GetByTestId("edit-importlist-modal");
        await Assertions.Expect(editAgain).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        var deleteTask = Page.WaitForResponseAsync(
            r => r.Url.Contains($"/api/v5/importlist/{definitionId}") && r.Request.Method == "DELETE",
            new PageWaitForResponseOptions { Timeout = 30_000 });

        await editAgain.GetByTestId("delete-button").ClickAsync();

        // The Edit modal dismisses before the confirm modal opens (WR-03 race).
        await Assertions.Expect(editAgain).ToBeHiddenAsync(
            new LocatorAssertionsToBeHiddenOptions { Timeout = 5_000 });

        // Scope the confirm Delete button to the open confirm dialog. The
        // page-level "Import List Exclusions" section always renders a
        // (disabled) Delete button, so an unscoped Name="Delete" locator
        // resolves to 2 elements (the disabled exclusions toolbar button +
        // the active confirm-dialog button). The confirm dialog is the only
        // dialog open at this point in the flow.
        var confirmDialog = Page.GetByRole(AriaRole.Dialog);
        var confirmDelete = confirmDialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete", Exact = true });
        await Assertions.Expect(confirmDelete).ToHaveCountAsync(
            1, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });
        await confirmDelete.ClickAsync();
        await deleteTask;

        // 5. State assertion: the renamed card is gone from the page.
        await Assertions.Expect(renamedCard).ToHaveCountAsync(
            0, new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
    }
}
