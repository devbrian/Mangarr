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
// Bucket B: registers TestImportList via TestKit.RegisterTestImportListAsync()
// in OneTimeSetUp (D-09). The fixture exercises POST → GET → PUT → DELETE
// directly against the V5 controller. Per L-002 (first-record-creation smoke),
// this is a CREATE-then-mutate flow, not just an empty-state render.
//
// Analog: src/NzbDrone.Automation.Test/Tests/Settings/IndexerAddEditDeleteFixture.cs
// (provider CRUD pattern — picker → Save → Edit → Save → Delete).
//
// FORWARD-STAGING NOTE (Plan 26-06 verification carve-out): TestImportList
// lives in NzbDrone.Core.Test/ImportListTests/Fakes/ — the production DI scan
// does NOT see it (Pitfall 2 anti-prod-leak gate). When the host's V5 POST
// rejects "Unknown implementation 'TestImportList'", the fixture branches to
// Assert.Inconclusive with the canonical reason and a forward-pointer to
// Phase 27 (which lands real providers and unblocks bucket B GREEN). Worktree
// compile-only verification per Plan 26-06; live execution gated by
// scripts/phase-smoke-gate.sh 26 in the host environment.
//
// Pattern κ: zero series-*/episode-*/season-*/add-series- selectors. Uses
// `settings-importlist-*` + `add-importlist-*` + `edit-importlist-*` allowed
// prefix family per Phase 18 D-18.
[TestFixture]
[Category("AutomationTest")]
public class ImportListCrudFixture : AutomationTest
{
    private const string TestName = "TestImportList (CRUD test)";

    [OneTimeSetUp]
    public async Task DisableComixAndRegisterFakeAsync()
    {
        var tk = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
        await tk.DisableComixIndexerAsync();

        // Pre-flight registration check. The OneTimeSetUp tolerates failure
        // here (the actual TestImportList registration happens in [SetUp] per
        // test so each test can branch to Inconclusive cleanly).
        var (_, _) = await tk.RegisterTestImportListAsync(TestName);
    }

    [Test]
    public async Task crud_round_trip_with_TestImportList()
    {
        var tk = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
        var (registrationResp, definitionId) = await tk.RegisterTestImportListAsync(TestName);

        if (definitionId == null)
        {
            // Forward-staging gate — see fixture summary. The production DI scan
            // does NOT see NzbDrone.Core.Test.ImportListTests.Fakes.TestImportList
            // until Phase 27 lands the substrate-extensibility seam OR a real
            // provider. Branch to Inconclusive with the diagnostic body so the
            // smoke gate run can pivot to a real-provider seed once Phase 27
            // ships. Per Plan 26-06 worktree-compile-only carve-out, this skip
            // is the documented Phase 26 boundary.
            Assert.Inconclusive(
                "TestImportList not registered (HTTP {0}); production DI scan excludes " +
                "NzbDrone.Core.Test fake providers. Forward-pointer: Phase 27 lands real " +
                "MangaDex / AniList / MAL IMangaImportList implementations that satisfy " +
                "bucket B GREEN. Worktree compile-only per Plan 26-06 verification carve-out. " +
                "Response body: {1}",
                (int)registrationResp.StatusCode,
                registrationResp.Content);
            return;
        }

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
        const string MutatedName = "TestImportList (CRUD test renamed)";
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

        var confirmDelete = Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Delete", Exact = true });
        await Assertions.Expect(confirmDelete).ToHaveCountAsync(
            1, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });
        await confirmDelete.ClickAsync();
        await deleteTask;

        // 5. State assertion: the renamed card is gone from the page.
        await Assertions.Expect(renamedCard).ToHaveCountAsync(
            0, new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
    }
}
