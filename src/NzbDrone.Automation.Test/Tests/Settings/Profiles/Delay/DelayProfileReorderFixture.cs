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
/// Creates two non-default profiles in insertion order (A then B), captures
/// pre-reorder Order values, then PUT /reorder/{A.id}?after={B.id} — moving A
/// AFTER B (opposite of natural insertion order). Asserts BOTH that the new
/// state matches the requested shape (B precedes A) AND that the underlying
/// Order values actually changed vs pre-reorder — so a no-op API implementation
/// cannot pass this test (CodeRabbit PR #198 finding 3255666023).
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

        // Capture pre-reorder Order values — A was inserted before B so naturally A.Order < B.Order.
        var preOrder = await ReadOrders(apiBase);
        preOrder.Should().ContainKey(idA);
        preOrder.Should().ContainKey(idB);
        preOrder[idA].Should().BeLessThan(preOrder[idB],
            "sanity: insertion order is A then B, so A.Order < B.Order before reorder");

        // PUT /reorder/{idA}?after={idB} — moves A AFTER B (flips the natural order).
        // This is the key test: a no-op API would leave A.Order < B.Order; a working
        // API must produce A.Order > B.Order.
        var reorderResp = await Page.APIRequest.PutAsync(
            $"{apiBase}/reorder/{idA}?after={idB}",
            new APIRequestContextOptions { DataObject = new { } });

        reorderResp.Status.Should().BeInRange(
            200,
            299,
            $"PUT /reorder should succeed; body: {await reorderResp.TextAsync()}");

        // Read back the ordered list and verify the reorder actually flipped the sequence
        // AND that the Order values genuinely changed (catches no-op API).
        var postOrder = await ReadOrders(apiBase);
        postOrder.Should().ContainKey(idA);
        postOrder.Should().ContainKey(idB);

        postOrder[idB].Should().BeLessThan(postOrder[idA],
            "PUT /reorder/{idA}?after={idB} must place B.Order strictly before A.Order");

        (postOrder[idA] != preOrder[idA] || postOrder[idB] != preOrder[idB])
            .Should().BeTrue(
                "at least one Order value must change post-reorder; a no-op API would leave both unchanged");
    }

    private async Task<Dictionary<int, int>> ReadOrders(string apiBase)
    {
        var listResp = await Page.APIRequest.GetAsync(apiBase);
        listResp.Status.Should().Be(200);
        var orderByEntry = new Dictionary<int, int>();
        foreach (var element in (await listResp.JsonAsync())!.Value.EnumerateArray())
        {
            var id = element.GetProperty("id").GetInt32();
            var order = element.GetProperty("order").GetInt32();
            orderByEntry[id] = order;
        }

        return orderByEntry;
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
