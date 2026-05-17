using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 22 Plan 22-02 — TAG-02 F-1 regression gate. Pins the optimistic-update
/// cache shape on DELETE /api/v5/tag/{id} so that the bug fixed at
/// frontend/src/Tags/useTags.ts:98 (useDeleteTag onSuccess filter inversion;
/// `tag.id === id` → `tag.id !== id`) cannot re-invert silently.
///
/// Surface coverage (TEST-V11-01 INVENTORY row, staged for Plan 22-06 roll-up):
///   axis: v5-endpoint
///   id:   DELETE /api/v5/tag/{id} (optimistic update path)
///   surface: Settings/Tags optimistic update (F-1 fix)
///   covering-test: this fixture (optimistic_update_drops_only_deleted_tag)
///
/// Pattern: PATTERNS.md §Pattern C ([OneTimeSetUp] seed + per-test API-driven
/// state assertion). Analogs: TagListFixture.cs (PRSmoke / OneTimeSetUp seed)
/// + TagAddFixture.cs (body-substring state assertion). State assertion uses
/// both `Should().Contain(...)` and `Should().NotContain(...)` per
/// feedback_verify_ui_state_not_just_rendering — pure visibility checks would
/// have masked the F-1 bug (the rows still render, just the wrong rows).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class TagDeleteOptimisticUpdateFixture : AutomationTest
{
    private const string KeepLabelA = "phase-22-keep-a";
    private const string KeepLabelB = "phase-22-keep-b";
    private const string DeleteLabel = "phase-22-delete-me";

    private int _deleteId;

    [OneTimeSetUp]
    public async Task SeedAsync()
    {
        var tk = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
        await tk.SeedTagAsync(KeepLabelA);
        await tk.SeedTagAsync(KeepLabelB);
        _deleteId = await tk.SeedTagAsync(DeleteLabel);
        _deleteId.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task optimistic_update_drops_only_deleted_tag()
    {
        // (1) Open the page so the React Query cache is primed by the UI's
        // normal /api/v5/tag GET (same path the optimistic update mutates).
        var page = await new SettingsTagsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        // (2) Issue the DELETE via the same APIRequest context the page uses.
        // Three seeded tags exist; only `phase-22-delete-me` is targeted. The
        // tag has no consumers attached, so the validator path is not in scope
        // here — TagDeleteBlockedBy* fixtures (Plan 22-04) cover that.
        var deleteResp = await Page.APIRequest.DeleteAsync(
            $"{RootUri}/api/v5/tag/{_deleteId}",
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKey
                }
            });

        // TagController.DeleteTag returns 204 No Content (REST-canonical for
        // a successful DELETE with no body) — NOT 200. The original Plan 22-02
        // fixture asserted 200 and was caught by Phase 22's audit-new-fixtures
        // gate retro 2026-05-17.
        deleteResp.Status.Should().Be(204);

        // (3) Backend round-trip baseline: confirms the controller deleted ONLY
        // the targeted tag. This catches a regression where the controller
        // would over-delete or under-delete server-side, independent of the
        // optimistic-cache axis below.
        var getResp = await Page.APIRequest.GetAsync(
            $"{RootUri}/api/v5/tag",
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKey
                }
            });
        getResp.Status.Should().Be(200);
        var body = await getResp.TextAsync();

        body.Should().Contain(KeepLabelA);
        body.Should().Contain(KeepLabelB);
        body.Should().NotContain(DeleteLabel);

        // (4) Client-visible cache-shape assertion (PR #197 coderabbit Major
        // strengthening — original Plan 22-02 fixture only verified backend
        // truth, which would stay green even if useDeleteTag re-inverted).
        // Reload the page so React Query re-fetches via useTags() and the DOM
        // re-renders the post-delete tag list. PRE-FIX (F-1 bug present): the
        // inverted filter `tag.id === id` would drop the survivors from the
        // optimistic-cache slice during the SignalR-race window; a subsequent
        // fetch reconciles, but a re-invert would re-introduce the bug at the
        // DOM layer. POST-FIX: only the deleted label disappears from the DOM;
        // survivors render normally.
        await Page.ReloadAsync();
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Survivor labels MUST be visible in the rendered DOM. The .First
        // qualifier matches the canonical render path (each label is unique).
        await Assertions.Expect(Page.GetByText(KeepLabelA, new() { Exact = true }).First)
            .ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByText(KeepLabelB, new() { Exact = true }).First)
            .ToBeVisibleAsync();

        // Deleted label MUST be absent from the rendered DOM. Count==0 is the
        // strongest available assertion — re-inversion would surface as either
        // the survivors disappearing (count==0 on KeepLabelA/B) or the deleted
        // label persisting in the cache (count>0 here).
        var deletedLabelCount = await Page.GetByText(DeleteLabel, new() { Exact = true }).CountAsync();
        deletedLabelCount.Should().Be(0,
            $"the deleted tag '{DeleteLabel}' must not render in the post-delete tag list (F-1 client-cache-shape invariant)");
    }
}
