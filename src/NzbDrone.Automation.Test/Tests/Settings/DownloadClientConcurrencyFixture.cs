using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Modals;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-05 — req-axis DOWNLOAD-02 (per-source concurrency configurable
/// in DownloadClient form).
///
/// Opens the baseline "InProcess (test seed)" client, fills the
/// InProcessDownloadsPerSource provider field (FieldDefinition Label per
/// InProcessImageDownloadClientSettings.cs:21 = "InProcessDownloadsPerSource"; no
/// en.json translation key exists so translate() falls back to the raw key), saves,
/// reopens, asserts the value round-tripped.
///
/// Blocker #4 path (b): baseline seed provides the InProcess client deterministically;
/// the field label was verified at Plan 20-05 Task 5.1 planning time. NO Inconclusive
/// fallback.
///
/// Tier (D-04): req axis is normally PR-smoke, but this fixture drives a full
/// modal Open -> Edit -> Save -> Reopen -> Read cycle = modal-action axis =
/// Nightly. Documented per D-04 axis-based tiering.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class DownloadClientConcurrencyFixture : AutomationTest
{
    [Test]
    public async Task concurrency_field_persists()
    {
        var page = await new SettingsDownloadClientsPage(Page).OpenAsync(RootUri);
        var card = page.CardByName("InProcess (test seed)");
        await Assertions.Expect(card).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await card.ClickAsync();

        var modal = new EditDownloadClientModal(Page);
        await Assertions.Expect(modal.ModalRoot).ToBeVisibleAsync();

        // Field label "InProcessDownloadsPerSource" was verified at Task 5.1
        // planning time against InProcessImageDownloadClientSettings.cs L21.
        // Validator range is 1..8 (InProcessImageDownloadClientSettingsValidator.cs:11).
        await Assertions.Expect(modal.MaxConcurrentDownloadsInput).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await modal.MaxConcurrentDownloadsInput.FillAsync("5");

        var putTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/downloadclient/") && r.Request.Method == "PUT",
            new() { Timeout = 30_000 });
        await modal.SaveButton.ClickAsync();
        var resp = await putTask;
        resp.Status.Should().BeInRange(200, 299);
        await Assertions.Expect(modal.ModalRoot).ToBeHiddenAsync(new() { Timeout = 15_000 });

        // Reopen card and assert the value persisted in the form.
        await card.ClickAsync();
        await Assertions.Expect(modal.ModalRoot).ToBeVisibleAsync(new() { Timeout = 15_000 });

        var value = await modal.MaxConcurrentDownloadsInput.InputValueAsync();
        value.Should().Be("5");
    }
}
