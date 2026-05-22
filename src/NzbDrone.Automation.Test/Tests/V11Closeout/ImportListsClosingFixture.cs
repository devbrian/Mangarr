using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.V11Closeout;

// Phase 28 Plan 28-01 Task 2 — V11Closeout per-vertical fixture.
//
// Vertical 5: ImportLists (Phase 26 substrate + Phase 27 3 providers +
// Phase 27.1 Sonarr-canonical parity sweep closing GH #220).
//
// Asserts the full Phase-27.1 toolbar + Options FieldSet + Exclusions Table
// shape is intact on a fresh DB:
//   - settings-importlists-page          (container — Phase 26)
//   - settings-importlist-add-card       (Add FieldSet card — Phase 26)
//   - settings-importlists-test-all-button   (Phase 27.1 toolbar parity)
//   - settings-importlists-manage-button     (Phase 27.1 Manage subtree)
//   - settings-importlists-options       (Options FieldSet — GH #220 close)
//   - settings-importlist-exclusion-mangadexid-header
//   - settings-importlist-exclusion-malid-header
//   - settings-importlist-exclusion-anilistid-header
//     (Phase 27.1 D-01 — 3-column manga-ID exclusion table replacing Sonarr's TvdbId)
//
// V5 API surface: GET /api/v5/importlist returns [] on fresh DB; the
// substrate is the FieldSet itself, not seeded rows (D-08 picker fills from
// Phase 27 provider plugins). GET /api/v5/importlistconfig (Phase 27.1
// dedicated config controller for Options FieldSet) returns 200.
[TestFixture]
[Category("AutomationTest")]
public class ImportListsClosingFixture : AutomationTest
{
    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).DisableComixIndexerAsync();
    }

    [Test]
    public async Task page_options_exclusions_and_toolbar_render_full_27_1_parity_shape()
    {
        var page = await new SettingsImportListsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // Toolbar parity (Phase 27.1 partial revocation of Phase 26 D-05)
        await Assertions.Expect(Page.GetByTestId("settings-importlists-test-all-button"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await Assertions.Expect(Page.GetByTestId("settings-importlists-manage-button"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // Options FieldSet (closes GH #220 — Phase 27.1). Per Phase 7 D-05
        // advanced-gating pattern (mirrored in ImportListOptionsAdvancedGatingFixture),
        // the Options FieldSet is conditionally rendered behind the
        // `settings-advanced-toggle` button; on a fresh DB the toggle may
        // boot to OFF. Normalise to "advanced ON" before asserting.
        var optionsContainer = Page.GetByTestId("settings-importlists-options");
        if (!(await optionsContainer.IsVisibleAsync()))
        {
            await Page.GetByTestId("settings-advanced-toggle").ClickAsync();
        }

        await Assertions.Expect(optionsContainer).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // Exclusions Table — 3 manga-ID column headers (Phase 27.1 D-01, mangaID triplet)
        await Assertions.Expect(Page.GetByTestId("settings-importlist-exclusion-mangadexid-header"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await Assertions.Expect(Page.GetByTestId("settings-importlist-exclusion-malid-header"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await Assertions.Expect(Page.GetByTestId("settings-importlist-exclusion-anilistid-header"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // V5 API surfaces — substrate alive
        var listResp = await Page.APIRequest.GetAsync($"{RootUri}/api/v5/importlist");
        listResp.Status.Should().Be(200, "Phase 26 ImportList substrate V5 controller");

        var configResp = await Page.APIRequest.GetAsync($"{RootUri}/api/v5/config/importlist");
        configResp.Status.Should().Be(200,
            "Phase 27.1 dedicated ImportListConfig V5 controller wires the Options FieldSet (route /api/v5/config/importlist per [V5ApiController(\"config/importlist\")])");

        var exclResp = await Page.APIRequest.GetAsync($"{RootUri}/api/v5/importlistexclusion");
        exclResp.Status.Should().Be(200, "Phase 26 ImportListExclusion V5 controller");
    }
}
