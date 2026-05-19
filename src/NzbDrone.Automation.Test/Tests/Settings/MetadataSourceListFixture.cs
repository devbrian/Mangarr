using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-07b (D-04 PRSmoke) — MetadataSource list-load fixture.
/// Greens INVENTORY v5-endpoint row
/// `GET /api/v5/metadatasource | Settings/MetadataSource list`.
///
/// Blocker #4 mitigation: seeds a MetadataSource via TestKit.SeedMetadataSourceAsync
/// so the list is guaranteed non-empty (MangaDex baseline is registered as a
/// schema option but no row is auto-seeded — fresh-DB-per-fixture per Phase 18 D-05).
///
/// PR #215 flake fix: the original implementation used
/// <c>Page.WaitForResponseAsync(...) + Page.ReloadAsync()</c> and read
/// <c>resp.TextAsync()</c>. That pair is racy — <c>ReloadAsync</c> can dispose
/// the prior page's network context before <c>TextAsync</c> issues its
/// <c>Network.getResponseBody</c> CDP call, producing intermittent
/// <c>"No resource with given identifier found"</c> failures
/// (microsoft/playwright-dotnet#1731 / #1840). Switched to the standard
/// <c>Page.APIRequest.GetAsync</c> pattern used by every other fixture in the
/// suite (see QueueRowDetailFixture / MangaMissingLanguageFilterFixture). That
/// API has its own response context independent of page navigation lifecycle.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class MetadataSourceListFixture : AutomationTest
{
    private const string SeedName = "Plan 20-07b MS List";

    [OneTimeSetUp]
    public async Task SeedAsync()
    {
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).SeedMetadataSourceAsync(SeedName);
    }

    [Test]
    public async Task list_loads()
    {
        // STATE assertion 1: the Settings/MetadataSource page renders without error.
        var page = await new SettingsMetadataSourcePage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync();

        // STATE assertion 2: the v5 endpoint returns the seeded row.
        // APIRequest is decoupled from page navigation, so the response body
        // survives any concurrent UI lifecycle (avoids the Network.getResponseBody
        // race that bit PR #215 — see class-level XML doc).
        var resp = await Page.APIRequest.GetAsync(
            $"{RootUri}/api/v5/metadatasource",
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKey
                }
            });

        resp.Status.Should().Be(200);
        var body = await resp.TextAsync();
        body.Should().Contain(SeedName);
    }
}
