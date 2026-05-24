using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using RestSharp;

namespace NzbDrone.Automation.Test.Tests.Settings.Metadata;

// Phase 30 Plan 30-04 Task 6 — Wave 0 API-only fixture for the new V5
// /api/v5/metadata controller (Plan 30-04 Task 3). Verifies:
//   1. GET /api/v5/metadata returns at least one row whose Implementation is
//      "ComicInfoMetadata" (auto-seeded by either Plan 30-05 Migration 004 D-03
//      OR by MetadataFactory.InitializeProviders defense-in-depth on first
//      boot — see Plan 30-04 Task 2). Enable defaults true (Migration 004 D-03
//      when Config.MetadataFormats contains "comicinfo", which is the Phase 4
//      default).
//   2. PUT /api/v5/metadata/{id} round-trips an Enable flip (true -> false ->
//      true). State assertion per `feedback_verify_ui_state_not_just_rendering`
//      — we re-GET after every PUT and inspect the parsed JSON Enable field.
//   3. GET /api/v5/metadata/schema returns the provider templates list (>=1
//      template for ComicInfoMetadata).
//   4. POST /api/v5/metadata/testall succeeds (ComicInfo has no network IO so
//      the always-valid Test() base default returns 200 OK).
//
// Closes II2-02 V5 endpoint smoke coverage. Analog pattern:
// IndexerActionEndpointApiFixture (API-only direct RestSharp + X-Api-Key).
// Not [Category("PRSmoke")] — per data-testid-spec / Phase 18 D-14: PRSmoke is
// reserved for top-nav route loads + AddManga happy path + SearchAndGrab happy
// path + one Settings save/load round-trip. This is a pure-API contract fixture
// (no Page interaction); the Settings/Metadata save/load round-trip is covered
// by MetadataSettingsFixture which IS the canonical PRSmoke entry for this surface.
[TestFixture]
[Category("AutomationTest")]
public class MetadataControllerFixture : AutomationTest
{
    [Test]
    public async Task list_returns_comicinfometadata_with_enable_true()
    {
        var client = new RestClient($"{RootUri}/api/v5");
        var request = new RestRequest("metadata", Method.GET);
        request.AddHeader("X-Api-Key", ApiKey);

        var response = await client.ExecuteAsync(request);
        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "GET /api/v5/metadata must respond 200 post-Plan-30-04 (closes Phase 15 404); body: " + response.Content);

        using var doc = JsonDocument.Parse(response.Content ?? "[]");
        var rows = doc.RootElement.EnumerateArray().ToList();
        rows.Should().NotBeEmpty(
            "InitializeProviders defense-in-depth auto-seeds ComicInfoMetadata when Migration 004 has not pre-seeded; both paths produce at least one row.");

        var comicInfo = rows.FirstOrDefault(e =>
            e.TryGetProperty("implementation", out var impl) &&
            impl.GetString() == "ComicInfoMetadata");

        comicInfo.ValueKind.Should().NotBe(
            JsonValueKind.Undefined,
            "Expected an `Implementation == \"ComicInfoMetadata\"` row in /api/v5/metadata; body: " + response.Content);

        // STATE assertion — Enable defaults true per Migration 004 D-03 seed
        // (Config.MetadataFormats default = [\"comicinfo\"] per Phase 4 D-14).
        comicInfo.GetProperty("enable").GetBoolean().Should().BeTrue(
            "Migration 004 D-03 seeds ComicInfoMetadata with Enable=true when Config.MetadataFormats defaulted to [\"comicinfo\"] (the Phase 4 default).");
    }

    [Test]
    public async Task put_toggle_round_trips_enable()
    {
        var client = new RestClient($"{RootUri}/api/v5");

        // 1. GET to find the ComicInfoMetadata row id + capture the full resource.
        var listReq = new RestRequest("metadata", Method.GET);
        listReq.AddHeader("X-Api-Key", ApiKey);
        var listResp = await client.ExecuteAsync(listReq);
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var listDoc = JsonDocument.Parse(listResp.Content ?? "[]");
        var comicInfo = listDoc.RootElement.EnumerateArray().First(e =>
            e.TryGetProperty("implementation", out var impl) &&
            impl.GetString() == "ComicInfoMetadata");
        var id = comicInfo.GetProperty("id").GetInt32();
        var resourceText = comicInfo.GetRawText();
        var originalEnable = comicInfo.GetProperty("enable").GetBoolean();

        // 2. PUT with Enable flipped to the opposite. RestSharp 106:
        // AddJsonBody(string) double-encodes; send as raw RequestBody verbatim.
        var flippedText = RewriteEnable(resourceText, !originalEnable);
        var putReq1 = new RestRequest($"metadata/{id}?skipTesting=true", Method.PUT);
        putReq1.AddHeader("X-Api-Key", ApiKey);
        putReq1.AddParameter("application/json", flippedText, ParameterType.RequestBody);
        var putResp1 = await client.ExecuteAsync(putReq1);
        ((int)putResp1.StatusCode).Should().BeInRange(
            200,
            299,
            $"PUT /api/v5/metadata/{id} must round-trip the Enable flip; body: " + putResp1.Content);

        // 3. STATE assertion — re-GET and verify the row now reflects the flip.
        var verifyReq1 = new RestRequest($"metadata/{id}", Method.GET);
        verifyReq1.AddHeader("X-Api-Key", ApiKey);
        var verifyResp1 = await client.ExecuteAsync(verifyReq1);
        verifyResp1.StatusCode.Should().Be(HttpStatusCode.OK);
        using var verifyDoc1 = JsonDocument.Parse(verifyResp1.Content ?? "{}");
        verifyDoc1.RootElement.GetProperty("enable").GetBoolean().Should().Be(
            !originalEnable,
            $"PUT /api/v5/metadata/{id} should have flipped Enable to {!originalEnable}; body: " + verifyResp1.Content);

        // 4. PUT it back to the original to leave state clean for any later fixtures.
        var restoredText = RewriteEnable(verifyResp1.Content!, originalEnable);
        var putReq2 = new RestRequest($"metadata/{id}?skipTesting=true", Method.PUT);
        putReq2.AddHeader("X-Api-Key", ApiKey);
        putReq2.AddParameter("application/json", restoredText, ParameterType.RequestBody);
        var putResp2 = await client.ExecuteAsync(putReq2);
        ((int)putResp2.StatusCode).Should().BeInRange(200, 299);

        // 5. Final STATE assertion — round-trip back to origin.
        var verifyReq2 = new RestRequest($"metadata/{id}", Method.GET);
        verifyReq2.AddHeader("X-Api-Key", ApiKey);
        var verifyResp2 = await client.ExecuteAsync(verifyReq2);
        using var verifyDoc2 = JsonDocument.Parse(verifyResp2.Content ?? "{}");
        verifyDoc2.RootElement.GetProperty("enable").GetBoolean().Should().Be(originalEnable);
    }

    [Test]
    public async Task schema_returns_comicinfometadata_template()
    {
        var client = new RestClient($"{RootUri}/api/v5");
        var req = new RestRequest("metadata/schema", Method.GET);
        req.AddHeader("X-Api-Key", ApiKey);

        var resp = await client.ExecuteAsync(req);
        resp.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "GET /api/v5/metadata/schema must respond 200; body: " + resp.Content);

        using var doc = JsonDocument.Parse(resp.Content ?? "[]");
        var templates = doc.RootElement.EnumerateArray().ToList();
        templates.Should().NotBeEmpty("Schema must contain at least the ComicInfoMetadata template post-Plan-30-04");

        var implementationNames = templates
            .Select(t => t.TryGetProperty("implementation", out var impl) ? impl.GetString() : null)
            .Where(n => n != null)
            .ToList();

        implementationNames.Should().Contain(
            "ComicInfoMetadata",
            "Schema must include the ComicInfoMetadata provider template; body: " + resp.Content);
    }

    [Test]
    public async Task testall_responds_ok_with_no_failures()
    {
        var client = new RestClient($"{RootUri}/api/v5");
        var req = new RestRequest("metadata/testall", Method.POST);
        req.AddHeader("X-Api-Key", ApiKey);

        var resp = await client.ExecuteAsync(req);

        // Test() defaults to always-valid for ComicInfoMetadata (no network IO);
        // ProviderControllerBase.TestAll returns Ok when all validations pass.
        ((int)resp.StatusCode).Should().BeInRange(
            200,
            299,
            "POST /api/v5/metadata/testall must respond 2xx (ComicInfo always-valid Test()); body: " + resp.Content);
    }

    // Rewrite the `"enable":<bool>` field in-place on a JSON resource string.
    // Uses System.Text.Json so the rest of the resource (Fields[], Tags, etc.)
    // round-trips verbatim.
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
