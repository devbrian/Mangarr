using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings.AutoTagging;

/// <summary>
/// Phase 24 Plan 24-05 — TagInUseValidator Slot 8 (AutoTagging) live coverage
/// fixture. Greens INVENTORY req row `AT-09 | Phase 22 D-03 stub-flip — Tag
/// delete rejected when in use by AutoTagging`.
///
/// Phase 22 D-03 documented the AutoTagging consumer #8 as stub-returns-false
/// awaiting Phase 24 flip. Plan 24-04 flipped the stub to
/// `_autoTaggingService.AllForTag(tag.Id).Count`; this fixture is the LIVE
/// end-to-end equivalent of the unit-level TagInUseValidatorAutoTaggingFixture
/// (Plan 24-04 NzbDrone.Core.Test).
///
/// Pattern 6 (24-PATTERNS.md §"Plan 24-05 patterns" lines 1156-1209): mirror
/// DelayProfileTagInUseFixture byte-for-byte with substitution: autotagging
/// route + "auto-tagging rule" error string.
///
/// Recipe:
///   1. Seed a tag.
///   2. POST an AutoTagging rule with tags=[{seedTagId}] -> 2xx.
///   3. Attempt DELETE /api/v5/tag/{tagId} -> 4xx (FluentValidation rejection).
///   4. Assert the error body mentions "auto-tagging rule" (the live consumer
///      summary string from TagInUseValidator.cs Slot 8 flip per Plan 24-04).
///
/// State-not-rendering: asserts the SPECIFIC 4xx status + the consumer-summary
/// substring, not just "tag-delete didn't return 204".
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class AutoTaggingTagInUseFixture : AutomationTest
{
    [Test]
    public async Task tag_in_use_by_auto_tagging_blocked_on_tag_delete()
    {
        var page = await new SettingsTagsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        var autoTaggingApi = $"{RootUri}/api/v5/autotagging";

        var tk = new NzbDrone.Automation.Test.TestKit.TestKit(RootUri, ApiKey, string.Empty);
        var tagLabel = $"plan-24-05-tu-{Guid.NewGuid():N}".Substring(0, 24);
        var tagId = await tk.SeedTagAsync(tagLabel);
        tagId.Should().BeGreaterThan(0);

        // POST an AutoTagging rule consuming the seeded tag.
        var createResp = await Page.APIRequest.PostAsync(autoTaggingApi, new APIRequestContextOptions
        {
            DataObject = new
            {
                name = $"tu-rule-{Guid.NewGuid():N}".Substring(0, 18),
                removeTagsAutomatically = false,
                tags = new[] { tagId },
                specifications = new[]
                {
                    new
                    {
                        name = "Shonen Demographic",
                        implementation = "DemographicSpecification",
                        implementationName = "Demographic",
                        negate = false,
                        required = false,
                        fields = new[]
                        {
                            new { name = "Value", value = 1 }
                        }
                    }
                }
            }
        });
        createResp.Status.Should().BeInRange(
            200,
            299,
            $"AutoTagging rule create with seeded tag must succeed; body: {await createResp.TextAsync()}");

        // Attempt DELETE on the tag. TagInUseValidator.cs Slot 8 should reject
        // with a 4xx + the consumer-summary substring "auto-tagging rule"
        // (Plan 24-04 stub-flip text exactly: "1 auto-tagging rule" /
        // "N auto-tagging rules").
        var tagDeleteResp = await Page.APIRequest.DeleteAsync($"{RootUri}/api/v5/tag/{tagId}");
        var tagDeleteBody = await tagDeleteResp.TextAsync();

        tagDeleteResp.Status.Should().BeInRange(
            400,
            499,
            $"DELETE /api/v5/tag/{tagId} must be rejected by TagInUseValidator (Phase 24 Plan 24-04 Slot 8 flip). " +
            $"Received status {tagDeleteResp.Status}; body: {tagDeleteBody}");

        // The consumer-summary string proves the AutoTagging branch fired
        // (vs another consumer rejecting it for a different reason). This is
        // the live-coverage equivalent of TagInUseValidatorAutoTaggingFixture
        // (Core.Test Plan 24-04) mock-Verify(AllForTag(tag.Id)).
        tagDeleteBody.Should().Contain(
            "auto-tagging rule",
            "the error message must surface 'auto-tagging rule' (live AutoTagging consumer summary " +
            "from TagInUseValidator.cs Slot 8 — Phase 22 D-03 stub-flip closed in Plan 24-04).");
    }
}
