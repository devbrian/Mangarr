using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel.Modals;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-04 (D-05) — Negative-validation fixture for the Indexer
/// vertical (+1 of +3 D-05 negatives; the other +2 land in 20-05 + 20-06).
///
/// gh178 sub-C (2026-05-16): the original "priority out of range" probe was
/// dead — NumberInput.parseValue clamps `999` to `max=50` BEFORE submit
/// (frontend/src/Components/Form/NumberInput.tsx:21), so the server-side
/// `SharedValidator.RuleFor(c => c.Priority).InclusiveBetween(1, 50)` never
/// gets to reject — POST returned 201 with priority=50. Switched the probe
/// to **empty Name**, which is server-validated by
/// `SharedValidator.RuleFor(c => c.Name).NotEmpty()` in
/// `ProviderControllerBase.cs:44` and has NO client-side clamp/HTML5
/// constraint — the Name TextInput accepts the empty string verbatim
/// and the POST body carries `name:""`, which the backend rejects with
/// 400 + propertyName=Name validation failure.
///
/// Tier (D-04): modal-action axis = Nightly (no [Category("PRSmoke")]).
/// NEW shape established here that Plans 20-05/06 mirror for DownloadClient
/// and Notification.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class IndexerNegativeValidationFixture : AutomationTest
{
    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
#pragma warning disable CS0618 // [reason: legacy pre-Phase-33; v1.3 audit per GH #XXX]
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).DisableComixIndexerAsync();
#pragma warning restore CS0618
    }

    [Test]
    public async Task save_with_empty_name_surfaces_validation_error()
    {
        await new SettingsIndexersPage(Page).OpenAsync(RootUri);
        await SettingsProviderFlow.OpenPickerAndSelectAsync(Page, "indexer", "mangadex");

        var modal = new EditIndexerModal(Page);

        // gh178 sub-C: clear Name (the schema preset is "MangaDex"). Name is
        // server-validated via SharedValidator.RuleFor(c => c.Name).NotEmpty()
        // (ProviderControllerBase.cs:44) and has no client-side guard.
        await modal.NameInput.FillAsync(string.Empty);

        // STATE assertion: POST /api/v5/indexer returns 400 (FluentValidation
        // failure on Name.NotEmpty) — the backend rejects the empty Name.
        var postTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/indexer") && r.Request.Method == "POST",
            new() { Timeout = 15_000 });

        await modal.SaveButton.ClickAsync();
        var resp = await postTask;
        resp.Status.Should().Be(
            400,
            "empty Name must be rejected server-side by SharedValidator.RuleFor(c => c.Name).NotEmpty()");

        // STATE assertion: modal stays open with the validation error visible.
        await Assertions.Expect(modal.ModalRoot).ToBeVisibleAsync(new() { Timeout = 5_000 });
    }
}
