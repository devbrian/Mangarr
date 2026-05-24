using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;
using RestSharp;

namespace NzbDrone.Automation.Test.Tests.Settings.Metadata;

// Phase 30 Plan 30-04 Task 6 — Wave 0 FE fixture verifying the Settings/Metadata
// page is fully functional (closes the long-standing 404 since Phase 15 deleted V3).
// Verifies:
//   1. /settings/metadata renders with the page-container testid mounted and the
//      "ComicInfo" provider name surfaced in the visible DOM (text content state
//      assertion — provider Card pulls `name` straight from /api/v5/metadata).
//   2. Toggling Enable via the API persists across a full page reload. STATE
//      assertion uses both the API response (parsed Enable field) AND the
//      visible Enabled/Disabled Label rendered by Metadata.tsx (kinds.SUCCESS vs
//      kinds.DISABLED).
//
// `feedback_verify_ui_state_not_just_rendering`: we re-query /api/v5/metadata
// after every state-change PUT and re-render check, rather than trusting
// visibility alone. The visible Label kind switches based on the parsed `enable`
// bool — confirming both the wire AND the FE consume cleanly.
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class MetadataSettingsFixture : AutomationTest
{
    [Test]
    public async Task settings_metadata_page_renders_comicinfo_row()
    {
        // 1. Navigate + assert the page container mounts (settings-metadata-page
        // testid lives in MetadataSettings.tsx).
        var page = await new SettingsMetadataPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        Page.Url.Should().MatchRegex(@"/settings/metadata$");

        // 2. STATE assertion — visible ComicInfo provider Card name text comes
        // from the /api/v5/metadata GET response that the React hook
        // useSortedMetadata wraps. The "ComicInfo" string is the
        // ComicInfoMetadata.Name override.
        var comicInfoLabel = page.PageContainer
            .GetByText("ComicInfo", new() { Exact = true })
            .First;
        await Assertions.Expect(comicInfoLabel).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        var nameText = await comicInfoLabel.TextContentAsync();
        nameText.Should().Be(
            "ComicInfo",
            "Provider Card name text content must equal the IMetadata.Name override exactly");

        // 3. STATE assertion — scope to the ComicInfo provider row so a future
        // multi-provider Settings page (v1.3+ Kodi/Komga NFO etc.) doesn't false-
        // positive on a different row's "Enabled" label. Row testid lives on the
        // Card itself per the Card.tsx:12-17 overlayContent contract.
        var (comicInfoId, comicInfoEnable, _) =
            await GetComicInfoAsync(new RestClient($"{RootUri}/api/v5"));
        var comicInfoRow = page.PageContainer
            .GetByTestId($"settings-metadata-item-{comicInfoId}");
        var expectedInitialLabel = comicInfoEnable ? "Enabled" : "Disabled";
        var enabledLabel = comicInfoRow.GetByText(expectedInitialLabel, new() { Exact = true });
        await Assertions.Expect(enabledLabel).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
    }

    [Test]
    public async Task enable_toggle_persists_across_reload()
    {
        // 1. Capture the initial Enable state via API (the visible UI is a render
        // of this state).
        var client = new RestClient($"{RootUri}/api/v5");
        var (initialId, initialEnable, initialResource) = await GetComicInfoAsync(client);

        try
        {
            // 2. Flip Enable to the opposite via the same V5 endpoint the FE Edit
            // modal would call on Save. This exercises the wire that backs the FE
            // toggle without depending on FE testid annotations the substrate
            // doesn't currently expose for the row itself.
            await PutEnableAsync(client, initialId, initialResource, !initialEnable);

            // 3. Navigate to /settings/metadata (fresh page render); the page
            // should render the post-flip state immediately because the React
            // Query hook fetches /api/v5/metadata on mount.
            var page = await new SettingsMetadataPage(Page).OpenAsync(RootUri);
            await Assertions.Expect(page.PageContainer).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

            // 4. STATE assertion — the visible Label on the ComicInfo row flips to
            // the post-flip variant. Scope to the row testid so a future multi-
            // provider Settings page can't false-positive on another row's label.
            var expectedLabelText = !initialEnable ? "Enabled" : "Disabled";
            var comicInfoRow = page.PageContainer
                .GetByTestId($"settings-metadata-item-{initialId}");
            var flippedLabel = comicInfoRow.GetByText(expectedLabelText, new() { Exact = true });
            await Assertions.Expect(flippedLabel).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

            // 5. Reload the page — confirms persistence across a fresh render
            // (not just SignalR push or stale Query cache).
            await Page.ReloadAsync();
            await Assertions.Expect(page.PageContainer).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
            var afterReloadRow = page.PageContainer
                .GetByTestId($"settings-metadata-item-{initialId}");
            var afterReloadLabel = afterReloadRow.GetByText(expectedLabelText, new() { Exact = true });
            await Assertions.Expect(afterReloadLabel).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

            // 6. STATE assertion via API (cross-confirm the visible Label is in
            // sync with the wire — guards against stale-render or testid-text
            // drift per `feedback_verify_ui_state_not_just_rendering`).
            var (_, postFlipEnable, _) = await GetComicInfoAsync(client);
            postFlipEnable.Should().Be(
                !initialEnable,
                "API-level Enable state must reflect the post-flip value after reload");
        }
        finally
        {
            // 7. Restore the original state so any later fixtures see a clean
            // baseline (initialEnable typically true for the seeded default).
            var (currentId, _, currentResource) = await GetComicInfoAsync(client);
            await PutEnableAsync(client, currentId, currentResource, initialEnable);
        }
    }

    private async Task<(int Id, bool Enable, string RawJson)> GetComicInfoAsync(RestClient client)
    {
        var req = new RestRequest("metadata", Method.GET);
        req.AddHeader("X-Api-Key", ApiKey);
        var resp = await client.ExecuteAsync(req);
        resp.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "GET /api/v5/metadata must respond 200; body: " + resp.Content);

        using var doc = JsonDocument.Parse(resp.Content ?? "[]");
        var comicInfo = doc.RootElement.EnumerateArray().First(e =>
            e.TryGetProperty("implementation", out var impl) &&
            impl.GetString() == "ComicInfoMetadata");

        return (
            comicInfo.GetProperty("id").GetInt32(),
            comicInfo.GetProperty("enable").GetBoolean(),
            comicInfo.GetRawText());
    }

    private async Task PutEnableAsync(RestClient client, int id, string resourceJson, bool newEnable)
    {
        var rewritten = RewriteEnable(resourceJson, newEnable);
        var req = new RestRequest($"metadata/{id}?skipTesting=true", Method.PUT);
        req.AddHeader("X-Api-Key", ApiKey);
        req.AddParameter("application/json", rewritten, ParameterType.RequestBody);
        var resp = await client.ExecuteAsync(req);
        ((int)resp.StatusCode).Should().BeInRange(
            200,
            299,
            $"PUT /api/v5/metadata/{id} must round-trip Enable={newEnable}; body: " + resp.Content);
    }

    private static string RewriteEnable(string resourceJson, bool newEnable)
    {
        using var doc = JsonDocument.Parse(resourceJson);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(prop.Name, "enable", StringComparison.OrdinalIgnoreCase))
                {
                    writer.WriteBoolean(prop.Name, newEnable);
                }
                else
                {
                    prop.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
