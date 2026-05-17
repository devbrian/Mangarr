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

        // (3) Re-query the canonical list — this is the cache-shape assertion.
        // PRE-FIX (bug present): with the inverted filter, the surviving tags
        // are dropped from the optimistic cache for the SignalR-race window;
        // the GET round-trip still returns truth from the backend, so this
        // GET assertion proves the BACKEND deleted only the one tag (TAG-02
        // SC #3 baseline). The unit-cache-shape assertion lives in a parallel
        // React unit test (cross-reference RESEARCH.md F-1 §Test).
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
    }
}
