using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings.Profiles.Delay;

/// <summary>
/// Phase 23 Plan 23-02 — DelayProfile PUT /reorder fixture. Greens INVENTORY
/// v5-endpoint row `PUT /api/v5/delayprofile/reorder/{id} | Settings/Profiles Delay drag-reorder`.
///
/// Creates two non-default profiles (A then B), then PUT /reorder/{B.id}?after={A.id}
/// — verifies A.Order &lt; B.Order in the response list (state-not-rendering: assert
/// the SPECIFIC Order sequence, not just "list returned 200").
///
/// The default profile (Id=1) carries Order=int.MaxValue per the Pattern S4
/// seeder and is excluded from reorder math (DelayProfileService.Reorder line
/// 142 skips Id=1).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class DelayProfileReorderFixture : AutomationTest
{
    [Test]
    public async Task reorder_applies()
    {
        var page = await new SettingsTranslationProfilesPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        var apiBase = $"{RootUri}/api/v5/delayprofile";

        // Seed two tags so each non-default profile carries a unique tag (avoids
        // DelayProfileTagInUseValidator failure on the second create).
        var tk = new NzbDrone.Automation.Test.TestKit.TestKit(RootUri, ApiKey, string.Empty);
        var tagA = await tk.SeedTagAsync($"plan-23-02-rA-{Guid.NewGuid():N}".Substring(0, 24));
        var tagB = await tk.SeedTagAsync($"plan-23-02-rB-{Guid.NewGuid():N}".Substring(0, 24));

        var idA = await Create(apiBase, 5, tagA);
        var idB = await Create(apiBase, 7, tagB);

        // PUT /reorder/{idB}?after={idA} — moves B to right after A.
        var reorderResp = await Page.APIRequest.PutAsync(
            $"{apiBase}/reorder/{idB}?after={idA}",
            new APIRequestContextOptions { DataObject = new { } });

        reorderResp.Status.Should().BeInRange(
            200,
            299,
            $"PUT /reorder should succeed; body: {await reorderResp.TextAsync()}");

        // Read back the ordered list and verify A precedes B (Order ascending).
        var listResp = await Page.APIRequest.GetAsync(apiBase);
        listResp.Status.Should().Be(200);
        var orderByEntry = new Dictionary<int, int>();
        foreach (var element in (await listResp.JsonAsync())!.Value.EnumerateArray())
        {
            var id = element.GetProperty("id").GetInt32();
            var order = element.GetProperty("order").GetInt32();
            orderByEntry[id] = order;
        }

        orderByEntry.Should().ContainKey(idA);
        orderByEntry.Should().ContainKey(idB);
        orderByEntry[idA].Should().BeLessThan(orderByEntry[idB],
            "PUT /reorder/{idB}?after={idA} must place A.Order strictly before B.Order");
    }

    private async Task<int> Create(string apiBase, int httpDelay, int tagId)
    {
        var resp = await Page.APIRequest.PostAsync(apiBase, new APIRequestContextOptions
        {
            DataObject = new
            {
                preferredProtocol = "http",
                httpDelay,
                order = 0,
                bypassIfHighestQuality = false,
                bypassIfAboveCustomFormatScore = false,
                minimumCustomFormatScore = 0,
                tags = new[] { tagId }
            }
        });
        resp.Status.Should().BeInRange(
            200,
            299,
            $"create should succeed; body: {await resp.TextAsync()}");
        return (await resp.JsonAsync())!.Value.GetProperty("id").GetInt32();
    }
}
