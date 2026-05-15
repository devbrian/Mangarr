using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Manga;

/// <summary>
/// Phase 20 Plan 20-08 (Wave 3 — Manga modal sweep) — INVENTORY modal-action row
/// `OrganizeMangaSelectModal` (MangaIndex bulk-select Organize).
///
/// Flow: 2-manga seed → select-mode → Select All → bulk Organize (the
/// "Rename Files" footer SpinnerButton; translate('RenameFiles') in
/// MangaIndexSelectFooter.tsx — the button label is "Rename Files" but the
/// modal-action row refers to the OrganizeMangaModal) → OrganizeSelectedManga
/// modal opens → confirm → POST /api/v5/command fires (the Organize bulk
/// path enqueues a RenameSeries command; isOrganizingSeries hook in the
/// footer).
///
/// State assertion: POST /api/v5/command returns 2xx (the RenameSeries
/// command was successfully enqueued).
///
/// Blocker #4: 2 manga seeded upfront; zero Inconclusive.
/// Pitfall 10: Comix disabled.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class MangaIndexBulkOrganizeFixture : AutomationTest
{
    private const string KnownMangaDexId  = AddMangaFlow.KnownMangaDexId;
    private const string KnownMangaDexId2 = AddMangaFlow.KnownMangaDexId2;

    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).DisableComixIndexerAsync();
    }

    [Test]
    public async Task bulk_organize()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId2);

        var index = await new MangaIndexPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(index.PageRoot).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Select Manga" }).First.ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Select All" }).First.ClickAsync();

        // The Organize bulk button text is "Rename Files" per
        // MangaIndexSelectFooter.tsx (translate('RenameFiles')).
        var organizeButton = Page.GetByRole(AriaRole.Button,
            new() { Name = "Rename Files", Exact = true }).First;
        await organizeButton.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await organizeButton.ClickAsync();

        var modal = Page.GetByRole(AriaRole.Dialog, new() { Name = "Organize Selected Manga" });
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // STATE assertion: confirm fires the POST /api/v5/command (RenameSeries).
        var cmdTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/command") && r.Request.Method == "POST",
            new PageWaitForResponseOptions { Timeout = 30_000 });

        // The confirm button on OrganizeMangaModalContent is the "Organize" Button
        // (kinds.DANGER) in ModalFooter.
        var confirmButton = modal.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions
        {
            Name = "Organize",
            Exact = false
        });
        await confirmButton.First.ClickAsync();

        var resp = await cmdTask;
        resp.Status.Should().BeInRange(
            200,
            299,
            "bulk Organize must enqueue a RenameSeries command (POST /api/v5/command)");
    }
}
