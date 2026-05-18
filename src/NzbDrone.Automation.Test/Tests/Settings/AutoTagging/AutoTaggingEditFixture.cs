using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings.AutoTagging;

/// <summary>
/// Phase 24 Plan 24-05 — AutoTagging PUT (edit) fixture. Greens INVENTORY
/// v5-endpoint row `PUT /api/v5/autotagging/{id} | Settings/Tags/Auto Tagging
/// Edit modal`.
///
/// Round-trips the `name` field via PUT and asserts GET returns the renamed
/// value. State-not-rendering: assert the SPECIFIC new name, not just
/// "rule still exists" (per feedback_verify_ui_state_not_just_rendering).
///
/// Pattern 6 (24-PATTERNS.md §"Plan 24-05 patterns"): mirror the
/// DelayProfileEditFixture round-trip-specific-field-value shape.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class AutoTaggingEditFixture : AutomationTest
{
    [Test]
    public async Task rename_roundtrips()
    {
        var page = await new SettingsTagsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        var apiBase = $"{RootUri}/api/v5/autotagging";

        var label = $"plan-24-05-edit-{Guid.NewGuid():N}".Substring(0, 24);
        var tk = new NzbDrone.Automation.Test.TestKit.TestKit(RootUri, ApiKey, string.Empty);
        var tagId = await tk.SeedTagAsync(label);
        tagId.Should().BeGreaterThan(0);

        var originalName = $"orig-{Guid.NewGuid():N}".Substring(0, 18);
        var renamedName = $"new-{Guid.NewGuid():N}".Substring(0, 18);

        // POST: create rule with originalName.
        var createResp = await Page.APIRequest.PostAsync(apiBase, new APIRequestContextOptions
        {
            DataObject = new
            {
                name = originalName,
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
        createResp.Status.Should().BeInRange(200, 299, $"POST should succeed; body: {await createResp.TextAsync()}");
        var created = (await createResp.JsonAsync())!.Value;
        var createdId = created.GetProperty("id").GetInt32();

        // PUT: rename to renamedName.
        var putResp = await Page.APIRequest.PutAsync($"{apiBase}/{createdId}", new APIRequestContextOptions
        {
            DataObject = new
            {
                id = createdId,
                name = renamedName,
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
        putResp.Status.Should().BeInRange(
            200,
            299,
            $"PUT should succeed; body: {await putResp.TextAsync()}");

        // GET single: state-not-rendering — assert the SPECIFIC new name.
        var getResp = await Page.APIRequest.GetAsync($"{apiBase}/{createdId}");
        getResp.Status.Should().Be(200);
        var fetched = (await getResp.JsonAsync())!.Value;
        fetched.GetProperty("name").GetString().Should().Be(renamedName,
            "PUT must rename the rule; GET must return the renamed value (state-not-rendering invariant)");
    }
}
