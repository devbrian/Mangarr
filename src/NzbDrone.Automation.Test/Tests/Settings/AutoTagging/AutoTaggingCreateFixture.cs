using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings.AutoTagging;

/// <summary>
/// Phase 24 Plan 24-05 — AutoTagging POST (create) fixture. Greens INVENTORY
/// v5-endpoint row `POST /api/v5/autotagging | Settings/Tags/Auto Tagging
/// Add modal`.
///
/// Mirrors Phase 23's DelayProfileAddFixture shape (Pattern 6 per
/// 24-PATTERNS.md line 1204). SharedValidator requires Name not-empty + Name
/// unique + Tags not-empty + Specifications not-empty + each
/// Specifications.Name not-whitespace; build the body to satisfy each.
///
/// Spec choice: DemographicSpecification with Value=1 (Shonen). The Demographic
/// spec is one of the 3 manga-NEW specs (AT-04 / D-04); using it here doubles
/// as a smoke that the manga-NEW catalog answers the controller round-trip.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class AutoTaggingCreateFixture : AutomationTest
{
    [Test]
    public async Task create_persists()
    {
        var page = await new SettingsTagsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        var apiBase = $"{RootUri}/api/v5/autotagging";

        // Seed a tag — every AutoTagging rule must apply at least one tag
        // (SharedValidator.RuleFor(c => c.Tags).NotEmpty() per AT-02 + Sonarr-
        // canonical contract).
        var label = $"plan-24-05-add-{Guid.NewGuid():N}".Substring(0, 24);
        var tk = new NzbDrone.Automation.Test.TestKit.TestKit(RootUri, ApiKey, string.Empty);
        var tagId = await tk.SeedTagAsync(label);
        tagId.Should().BeGreaterThan(0);

        var ruleName = $"plan-24-05-rule-{Guid.NewGuid():N}".Substring(0, 28);

        var createBody = new
        {
            name = ruleName,
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
        };

        var createResp = await Page.APIRequest.PostAsync(apiBase, new APIRequestContextOptions
        {
            DataObject = createBody
        });

        createResp.Status.Should().BeInRange(
            200,
            299,
            $"POST should succeed; received body: {await createResp.TextAsync()}");

        var createdJson = await createResp.JsonAsync();
        createdJson.HasValue.Should().BeTrue();
        var createdId = createdJson!.Value.GetProperty("id").GetInt32();
        createdId.Should().BeGreaterThan(0);

        // State assertion: GET list now contains the created rule by id.
        var listResp = await Page.APIRequest.GetAsync(apiBase);
        listResp.Status.Should().Be(200);
        var listJson = await listResp.JsonAsync();
        var foundCreated = false;
        var foundName = string.Empty;
        foreach (var element in listJson!.Value.EnumerateArray())
        {
            if (element.TryGetProperty("id", out var idProp) && idProp.GetInt32() == createdId)
            {
                foundCreated = true;
                element.TryGetProperty("name", out var nameProp);
                foundName = nameProp.GetString() ?? string.Empty;
                break;
            }
        }

        foundCreated.Should().BeTrue("created AutoTagging rule must appear in GET list");
        foundName.Should().Be(ruleName, "round-tripped name must match the POST body (state-not-rendering invariant)");
    }
}
