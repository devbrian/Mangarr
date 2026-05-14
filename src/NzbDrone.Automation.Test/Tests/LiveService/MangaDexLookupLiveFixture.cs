using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.LiveService;

/// <summary>
/// Phase 18 D-10 LiveService tier — MangaDex contract-invariant nightly probe.
///
/// Direct HTTPS call to api.mangadex.org (bypasses the Mangarr backend) so the test
/// asserts on the REAL upstream API shape, not Mangarr's interpretation. When this
/// fixture goes red overnight, a human reviews the diff, decides whether to update
/// Mangarr code to match, then runs the cassette-capture script to refresh offline
/// cassettes used by the (D-10) offline tier.
///
/// Excluded from PR CI smoke via the test-category filter
/// `TestCategory=AutomationTest&amp;TestCategory!=LiveService` (D-14); included in
/// Plan-10's `automation_test_liveservice` nightly workflow.
///
/// T-18-04 mitigation (DoS/rate-limit posture): nightly cadence only; single
/// known-good ID (Komi Can't Communicate — does not crawl); honest User-Agent
/// identifies us to upstream operators for rate-limit allowlist negotiation.
/// T-18-02 disposition: intentional bypass of backend IHttpClient per D-10 design
/// — LiveService tier is the upstream-contract probe by definition.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("LiveService")]
public class MangaDexLookupLiveFixture : AutomationTest
{
    // Komi Can't Communicate — stable manga with chronic publication history; chosen as the
    // known-good ID for contract-invariant probing (no value-bound assertions on the row data).
    private const string KnownMangaDexId = "a96676e5-8ae2-425e-b549-7f15dd34a6d8";

    [Test]
    public async Task mangadex_chapter_feed_response_has_publishAt_iso8601_field()
    {
        // D-10 contract-invariant: ensure MangaDex hasn't renamed publishAt or removed it.
        // Assertion shape is contract-invariant (not value-bound): existence + ISO8601 parse.
        // When MangaDex breaks this, we update Mangarr's MangaDexMetadataSource mapping and
        // re-record the offline cassette tier — without this nightly probe the breakage would
        // only surface during user-facing chapter-list rendering.
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add(
            "User-Agent",
            "Mangarr-CI/1.0 (https://github.com/devbrian/Mangarr; LiveService nightly contract probe)");

        var response = await http.GetAsync(
            $"https://api.mangadex.org/manga/{KnownMangaDexId}/feed?limit=5");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        // STATE assertion: data[].attributes.publishAt exists and parses as DateTime.
        var data = doc.RootElement.GetProperty("data");
        data.GetArrayLength().Should().BeGreaterThan(
            0,
            "Komi Can't Communicate has chapters in the MangaDex catalog");

        var first = data[0];
        var attrs = first.GetProperty("attributes");
        var publishAt = attrs.GetProperty("publishAt").GetString();
        publishAt.Should().NotBeNullOrEmpty();
        DateTime.TryParse(publishAt, out _).Should()
            .BeTrue($"publishAt must be ISO8601 (was '{publishAt}')");
    }
}
