using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings.AutoTagging;

/// <summary>
/// Phase 24 Plan 24-05 — AutoTagging DELETE fixture. Greens INVENTORY
/// v5-endpoint row `DELETE /api/v5/autotagging/{id} | Settings/Tags/Auto
/// Tagging row delete`.
///
/// Creates a rule then DELETEs it. Asserts:
///   1. Successful DELETE returns 2xx.
///   2. Subsequent GET list no longer includes the deleted id
///      (state-not-rendering invariant).
///
/// Pattern 6 (24-PATTERNS.md §"Plan 24-05 patterns"): mirror the
/// DelayProfileDeleteFixture shape.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class AutoTaggingDeleteFixture : AutomationTest
{
    [Test]
    public async Task delete_removes_row()
    {
        var page = await new SettingsTagsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        var apiBase = $"{RootUri}/api/v5/autotagging";

        var label = $"plan-24-05-del-{Guid.NewGuid():N}".Substring(0, 24);
        var tk = new NzbDrone.Automation.Test.TestKit.TestKit(RootUri, ApiKey, string.Empty);
        var tagId = await tk.SeedTagAsync(label);

        var createResp = await Page.APIRequest.PostAsync(apiBase, new APIRequestContextOptions
        {
            DataObject = new
            {
                name = $"del-{Guid.NewGuid():N}".Substring(0, 18),
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
                            new { name = "value", value = 1 }
                        }
                    }
                }
            }
        });
        createResp.Status.Should().BeInRange(200, 299, $"POST should succeed; body: {await createResp.TextAsync()}");
        var createdId = (await createResp.JsonAsync())!.Value.GetProperty("id").GetInt32();

        // DELETE.
        var deleteResp = await Page.APIRequest.DeleteAsync($"{apiBase}/{createdId}");
        deleteResp.Status.Should().BeInRange(
            200,
            299,
            $"DELETE should succeed for AutoTagging rule Id={createdId}; body: {await deleteResp.TextAsync()}");

        // Subsequent GET list does not contain the deleted id.
        var listResp = await Page.APIRequest.GetAsync(apiBase);
        listResp.Status.Should().Be(200);
        var listJson = await listResp.JsonAsync();
        var foundDeleted = false;
        foreach (var element in listJson!.Value.EnumerateArray())
        {
            if (element.TryGetProperty("id", out var idProp) && idProp.GetInt32() == createdId)
            {
                foundDeleted = true;
                break;
            }
        }

        foundDeleted.Should().BeFalse(
            "deleted AutoTagging rule must NOT appear in subsequent GET list (state-not-rendering invariant)");
    }
}
