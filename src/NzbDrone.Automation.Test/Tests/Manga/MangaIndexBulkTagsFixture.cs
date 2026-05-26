using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.Manga;

/// <summary>
/// Phase 20 Plan 20-08 (Wave 3 — Manga modal sweep) — INVENTORY modal-action row
/// `TagsSelectModal` (MangaIndex bulk-select Tags).
///
/// Flow: 2-manga seed → select-mode → Select All → bulk Set Tags (the
/// "Set Tags" footer SpinnerButton; translate('SetTags') in
/// MangaIndexSelectFooter.tsx) → Tags modal opens (ModalHeader 'Tags' per
/// TagsModalContent.tsx) → confirm Apply → PUT /api/v5/manga/editor fires.
///
/// State assertion: PUT /api/v5/manga/editor returns 2xx AND modal closes.
///
/// Blocker #4: 2 manga seeded upfront; zero Inconclusive.
/// Pitfall 10: Comix disabled.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class MangaIndexBulkTagsFixture : AutomationTest
{
    private const string KnownMangaDexId  = AddMangaFlow.KnownMangaDexId;
    private const string KnownMangaDexId2 = AddMangaFlow.KnownMangaDexId2;

    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
#pragma warning disable CS0618 // [reason: legacy pre-Phase-33; v1.3 audit per GH #XXX]
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).DisableComixIndexerAsync();
#pragma warning restore CS0618
    }

    [Test]
    public async Task bulk_tags()
    {
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId);
        await AddMangaFlow.AddByMangaDexIdAsync(Page, RootUri, KnownMangaDexId2);

        var index = await new MangaIndexPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(index.PageRoot).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Select Manga" }).First.ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Select All" }).First.ClickAsync();

        // The bulk-tags footer button is labelled "Set Tags" per
        // MangaIndexSelectFooter.tsx (translate('SetTags')).
        var tagsButton = Page.GetByRole(AriaRole.Button, new() { Name = "Set Tags", Exact = true }).First;
        await tagsButton.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await tagsButton.ClickAsync();

        // The Tags modal header is just 'Tags' per TagsModalContent.tsx.
        var modal = Page.GetByRole(AriaRole.Dialog, new() { Name = "Tags" });
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // STATE assertion: clicking Apply inside the dialog fires PUT /api/v5/manga/editor
        // (the saveMangaEditor flow per onApplyTagsPress in MangaIndexSelectFooter.tsx).
        var putTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("/api/v5/manga/editor") && r.Request.Method == "PUT",
            new PageWaitForResponseOptions { Timeout = 30_000 });

        var applyButton = modal.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions
        {
            Name = "Apply",
            Exact = false
        });
        await applyButton.First.ClickAsync();

        var resp = await putTask;
        resp.Status.Should().BeInRange(
            200,
            299,
            "PUT /api/v5/manga/editor must round-trip cleanly");

        // STATE assertion: Tags modal hides after save-success (the footer
        // setIsTagsModalOpen(false) effect on onApplyTagsPress).
        await Assertions.Expect(modal).ToBeHiddenAsync(new() { Timeout = 10_000 });
    }
}
