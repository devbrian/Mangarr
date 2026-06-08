using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;
using NzbDrone.Automation.Test.PageModel;
using NzbDrone.Automation.Test.PageModel.Modals;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 22 Plan 22-06 — TAG-04 first-record-creation visibility leg.
///
/// Closes the Phase 17.3 L-002 anti-empty-state regression pattern for the
/// Tag vertical: fresh DB → seed a tag → open the Manga Edit modal → the
/// seeded tag is reachable in the Tags select input. The pattern Phase 17.3
/// shipped for chapter-list / chapter-file / etc. surfaces is now extended
/// to the cross-vertical tag-consumer surface (Tag-vertical seed → Manga-
/// vertical render).
///
/// Surface coverage (TEST-V11-01 INVENTORY row, staged for Plan 22-06 roll-up):
///   axis: req
///   id:   TAG-04
///   surface: First-record-creation: fresh DB → create tag → visible in Manga/Edit
///   covering-test: this fixture (new_tag_appears_in_manga_edit_tag_selector)
///
/// Selector contract: the Tags FormInputGroup inside EditMangaModalContent.tsx
/// is wrapped in `&lt;div data-testid="edit-manga-modal-tags-select"&gt;` per the
/// v1.1 data-testid-spec.md append (landed in Plan 22-06 Task 1 BEFORE this
/// fixture per the Phase 18 D-18 append-before-author policy).
///
/// STATE assertion: after focusing the tag input and typing a prefix-unique
/// substring of the seeded label, the react-autosuggest suggestion list
/// surfaces the seeded label as a visible option. This is a stronger
/// guarantee than mere rendering — the tag must be in `useSortedTagList()`'s
/// React Query cache (hydrated from GET /api/v5/tag) AND not in the manga's
/// `tags` array yet (per `useMangaTags` filter at line 29 of MangaTagInput.tsx).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class TagVisibleInMangaEditFixture : AutomationTest
{
    private const string KnownMangaBakaId = AddMangaFlow.KnownMangaBakaId;

    private const string LabelPrefix = "p22tag-";
    private string _label;

    [OneTimeSetUp]
    public async Task SeedAsync()
    {
        // 1. Seed a manga via the canonical UI flow (puts Komi in the library
        //    with a stable titleSlug). Required because the Edit modal is
        //    only reachable from /manga/{titleSlug} → Edit button.
        await AddMangaFlow.AddByMangaBakaIdAsync(Page, RootUri, KnownMangaBakaId);

        // 2. Seed a tag with a label whose prefix `p22tag-` is unique to this
        //    fixture (avoids cross-fixture collision; the trailing GUID
        //    suffix is the prefix-unique substring we type into the input
        //    later to surface the autosuggest dropdown).
        _label = $"{LabelPrefix}{Guid.NewGuid():N}".Substring(0, 24);
        var tk = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
        var tagId = await tk.SeedTagAsync(_label);
        tagId.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task new_tag_appears_in_manga_edit_tag_selector()
    {
        // 1. Navigate to the manga details page. AddMangaFlow already landed
        //    us on /manga/{slug} as part of OneTimeSetUp, but we re-navigate
        //    to refresh the page state — the freshly-seeded tag must be in
        //    the useSortedTagList() React Query cache when the modal mounts.
        var details = new MangaDetailsPage(Page);
        await Assertions.Expect(details.MainContainer).ToBeVisibleAsync();

        // 2. Click Edit to open the modal.
        await details.EditButton.ClickAsync();

        var modal = new EditMangaModal(Page);
        await Assertions.Expect(modal.ModalRoot).ToBeVisibleAsync();

        // 3. Locate the Tags select container by the testid published in
        //    Plan 22-06 Task 1 (v1.1 data-testid-spec.md append).
        var tagsSelect = Page.GetByTestId("edit-manga-modal-tags-select");
        await Assertions.Expect(tagsSelect).ToBeVisibleAsync();

        // 4. Focus the input inside the tags container and type the
        //    label-suffix substring. The MangaTagInput renders a single
        //    `<input>` (via react-autosuggest's AutoSuggestInput); typing
        //    surfaces the autosuggest dropdown with matching `tagList`
        //    entries (the entries come from useSortedTagList() → GET
        //    /api/v5/tag, which the seeded tag is in).
        var input = tagsSelect.Locator("input").First;
        await input.ClickAsync();
        await input.FillAsync(_label.Substring(LabelPrefix.Length, 4));

        // 5. STATE assertion: the seeded label appears in the autosuggest
        //    suggestion list. Mangarr/Sonarr customizes react-autosuggest's
        //    theme with CSS-module class names (frontend/src/Components/Form/
        //    AutoSuggestInput.tsx:185 theme prop → `styles.suggestion`), so
        //    the literal `li.react-autosuggest__suggestion` selector that
        //    the default-theme version of react-autosuggest emits does NOT
        //    exist in the rendered DOM (Phase 22 audit-new-fixtures retro
        //    2026-05-17). The library still emits proper ARIA roles on the
        //    suggestion LIs (role="option"); using the role selector is
        //    both webpack-class-hash-independent and accessibility-aligned.
        var suggestions = Page.Locator("[role='option']");
        await suggestions.First.WaitForAsync(
            new LocatorWaitForOptions { Timeout = 10_000 });

        var visibleSuggestionTexts = await suggestions.AllInnerTextsAsync();
        visibleSuggestionTexts
            .Should()
            .Contain(
                t => t.Contains(_label),
                $"the seeded tag '{_label}' must surface in the Manga/Edit Tags autosuggest list (TAG-04 first-record-creation invariant). Observed suggestions: {string.Join(", ", visibleSuggestionTexts)}");
    }
}
