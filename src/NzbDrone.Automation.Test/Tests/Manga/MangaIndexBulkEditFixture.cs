using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Manga;

/// <summary>
/// Phase 20 Plan 20-08 (Wave 3 — Manga modal sweep) — INVENTORY modal-action row
/// `EditMangaSelectModal` (MangaIndex bulk-select Edit).
///
/// Flow: 2-manga seed → select-mode → Select All → bulk Edit (the "Edit" footer
/// SpinnerButton; translate('Edit') in MangaIndexSelectFooter.tsx) → modal opens
/// with EditSelectedManga header → click Save → PUT /api/v5/manga/editor fires.
///
/// State assertion: PUT /api/v5/manga/editor returns 2xx AND modal closes (modal
/// auto-closes via the saveMangaEditor onSuccess effect). The MangaIndexBulkActions
/// canonical fixture short-circuits this path at the "modal opens" gate; this
/// fixture closes the loop by actually saving.
///
/// Blocker #4: 2 manga seeded upfront; zero Inconclusive.
/// Pitfall 10: Comix disabled.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class MangaIndexBulkEditFixture : AutomationTest
{
    private const string KnownMangaDexId  = AddMangaFlow.KnownMangaDexId;
    private const string KnownMangaDexId2 = AddMangaFlow.KnownMangaDexId2;

    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).DisableComixIndexerAsync();
    }

    [Test]
    public async Task bulk_edit()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId2);

        var index = await new MangaIndexPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(index.PageRoot).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Select Manga" }).First.ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Select All" }).First.ClickAsync();

        var editButton = Page.GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true }).First;
        await editButton.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await editButton.ClickAsync();

        var modal = Page.GetByRole(AriaRole.Dialog, new() { Name = "Edit Selected Manga" });
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // debug-30 (2026-05-16): EditMangaModalContent.save() short-circuits
        // when no field changed — no onSavePress → no PUT. Flip Monitored
        // to a non-NO_CHANGE value so the PUT actually fires.
        // debug-30 iter-2 (2026-05-16): EnhancedSelectInput options render
        // in <FloatingPortal id="portal-root"> (SIBLING of modal, not child).
        // Scope to #portal-root to avoid colliding with the in-modal
        // <FormLabel>"Monitored" (non-interactive — clicking it is a no-op).
        var monitoredTrigger = modal.GetByText("No Change").First;
        await monitoredTrigger.ClickAsync();
        var monitoredOption = Page.Locator("#portal-root")
            .GetByText("Monitored", new() { Exact = true })
            .First;
        await Assertions.Expect(monitoredOption).ToBeVisibleAsync(new() { Timeout = 5_000 });
        await monitoredOption.ClickAsync();

        // STATE assertion: clicking Save inside the dialog fires PUT /api/v5/manga/editor.
        var putTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/manga/editor") && r.Request.Method == "PUT",
            new PageWaitForResponseOptions { Timeout = 30_000 });

        var applyButton = modal.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions
        {
            Name = "Apply Changes",
            Exact = false
        });

        // If "Apply Changes" wasn't rendered (label wording variance), fall back to "Save".
        if (await applyButton.CountAsync() == 0)
        {
            applyButton = modal.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions
            {
                Name = "Save",
                Exact = false
            });
        }

        await applyButton.First.ClickAsync();

        var resp = await putTask;
        resp.Status.Should().BeInRange(
            200,
            299,
            "PUT /api/v5/manga/editor must round-trip cleanly");

        // STATE assertion: modal closes (Edit bulk modal closes itself on save-success).
        await Assertions.Expect(modal).ToBeHiddenAsync(new() { Timeout = 10_000 });
    }
}
