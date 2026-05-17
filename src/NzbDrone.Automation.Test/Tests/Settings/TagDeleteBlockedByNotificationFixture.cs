using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 22 Plan 22-06 — TAG-03 D-06 Notification consumer leg.
///
/// Structural twin of TagDeleteBlockedByMangaFixture (this phase's sibling)
/// with the consumer-axis swap: Notification (Connection) instead of Manga.
/// Pins the TagInUseValidator (Plan 22-04) DELETE-path 400 gate when a tag
/// is held by a Connection resource. Validator emits the joined-consumer-
/// summary error format "1 connection" (D-06 frontend label per the
/// ConnectionResource resource-name convention — the user-friendly i18n
/// string is "connection", not the internal "Notification" type name).
///
/// Surface coverage (TEST-V11-01 INVENTORY row, staged for Plan 22-06 roll-up):
///   axis: v5-endpoint
///   id:   DELETE /api/v5/tag/{id}
///   surface: Settings/Tags TagInUseValidator path (Notification consumer)
///   covering-test: this fixture (delete_blocked_when_in_use_by_notification)
///
/// Pattern: PATTERNS.md lines 268..276 (structural twin + consumer-axis
/// adaptation). TestKit ships SeedNotificationAsync but it doesn't accept a
/// tags array — Plan 22-06 deliberately does NOT extend TestKit; this
/// fixture uses an inline POST to /api/v5/connection?skipTesting=true with
/// the tags field populated.
///
/// STATE assertions per feedback_verify_ui_state_not_just_rendering:
///   1. DELETE response status == 400 (validator-driven 4xx)
///   2. response body contains literal "1 connection" (D-06 frontend label
///      per ConnectionResource convention)
///   3. subsequent GET /api/v5/tag/{tagId} returns 200 — pre-side-effect
///      (Pattern B explicit-pre-check rejected the DELETE before
///      _tagService.Delete(id) ran in TagController.DeleteTag).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class TagDeleteBlockedByNotificationFixture : AutomationTest
{
    private int _tagId;

    [OneTimeSetUp]
    public async Task SeedAsync()
    {
        // 1. Seed a per-run-unique tag (label-length capped at 24 chars to
        //    keep within Tag.Label validation regex `^[a-z0-9-]+$`).
        var label = $"phase-22-block-not-{Guid.NewGuid():N}".Substring(0, 24);
        var tk = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
        _tagId = await tk.SeedTagAsync(label);
        _tagId.Should().BeGreaterThan(0);

        // 2. Seed a Komga Notification (Connection) inline with the tag
        //    attached. The TestKit SeedNotificationAsync helper does not
        //    accept a tags array; per Plan 22-06 PATTERNS.md line 275 we
        //    use the inline POST fallback (we deliberately do NOT extend
        //    TestKit in this plan to keep the Phase 22 close-out diff
        //    narrowly scoped).
        //
        //    Body shape mirrors TestKit.SeedNotificationAsync verbatim
        //    except for the appended `tags` field; the underlying V5
        //    surface is POST /api/v5/connection?skipTesting=true (the
        //    legacy "notification" resource was retired in Phase 15 Plan
        //    15-10 — see TestKit.cs:333 comment + gh #187).
        var notificationBody = JsonSerializer.Serialize(new
        {
            onGrab = false,
            onDownload = false,
            onUpgrade = false,
            onChapterImport = true,
            onRename = false,
            onMangaAdd = false,
            onMangaDelete = false,
            onHealthIssue = false,
            includeHealthWarnings = false,
            name = $"Komga (phase-22 tag-{_tagId})",
            implementation = "KomgaNotification",
            configContract = "KomgaNotificationSettings",
            tags = new[] { _tagId },
            fields = new object[]
            {
                new { name = "url", value = "http://komga.local:25600" },
                new { name = "apiKey", value = "testkit-placeholder-key" },
                new { name = "libraryId", value = 1 }
            }
        });

        var postResp = await Page.APIRequest.PostAsync(
            $"{RootUri}/api/v5/connection?skipTesting=true",
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKey,
                    ["Content-Type"] = "application/json"
                },
                DataString = notificationBody
            });
        var postBody = await postResp.TextAsync();
        new[] { 200, 201 }.Should().Contain(postResp.Status,
            $"POST /api/v5/connection should accept the tagged Notification seed; observed: {postResp.Status} — body: {postBody}");
    }

    [Test]
    public async Task delete_blocked_when_in_use_by_notification()
    {
        // STATE assertion #1 — DELETE returns 400 (validator path engaged).
        var deleteResp = await Page.APIRequest.DeleteAsync(
            $"{RootUri}/api/v5/tag/{_tagId}",
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKey
                }
            });
        deleteResp.Status.Should().Be(400);

        // STATE assertion #2 — error body names the Notification consumer
        // cohort. D-06 frontend label per RESEARCH.md §Sonarr Canon §Error
        // message format: lowercase "1 connection" (ConnectionResource
        // resource-name convention; the user-facing label is "connection"
        // even though the internal type name is "Notification").
        var deleteBody = await deleteResp.TextAsync();
        deleteBody.Should().Contain("1 connection");

        // STATE assertion #3 — pre-side-effect: the tag still exists.
        var getResp = await Page.APIRequest.GetAsync(
            $"{RootUri}/api/v5/tag/{_tagId}",
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKey
                }
            });
        getResp.Status.Should().Be(200);
    }
}
