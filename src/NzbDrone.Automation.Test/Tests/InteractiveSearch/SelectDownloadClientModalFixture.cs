using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.Tests.InteractiveSearch;

/// <summary>
/// Phase 20 Plan 20-10 (Wave 3 InteractiveSearch sweep) — modal-action axis
/// SelectDownloadClientModal (INVENTORY row 170 — OverrideMatch DownloadClient
/// selector).
///
/// Tier (D-04): Nightly default — modal-action axis per the mechanical
/// row-axis rule.
///
/// **Blocker #4 path c (V1-not-wired UI surface):** SelectDownloadClientModal
/// is mounted inside OverrideMatchModal (per InteractiveSearch/OverrideMatch/
/// DownloadClient/SelectDownloadClientModal.tsx); the only canonical entry
/// path is OverrideMatchModal → DownloadClient picker, which itself has no
/// per-row testid wired in v1 (only the title-attribute Link triggers).
/// Per Plan 20-04 / 20-07b / 20-08 / 20-09 path c precedent — assert the
/// download client API contract that the modal will read from is reachable:
/// GET /api/v5/downloadclient returns the seeded InProcess client.
///
/// State assertion (per feedback_verify_ui_state_not_just_rendering):
///   1. GET /api/v5/downloadclient returns 200 with the seeded InProcess
///      client present (the data source the modal consumes).
///   2. Response carries a `name` field per the DownloadClient resource
///      shape — proves the modal will have a non-empty picker list.
///
/// Pitfall 10: Comix disabled in OneTimeSetUp.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class SelectDownloadClientModalFixture : AutomationTest
{
    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
#pragma warning disable CS0618 // [reason: legacy pre-Phase-33; v1.3 audit per GH #XXX]
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty)
            .DisableComixIndexerAsync();
#pragma warning restore CS0618
    }

    [Test]
    public async Task client_select()
    {
        // SelectDownloadClientModal consumes the GET /api/v5/downloadclient
        // listing to render the picker. The baseline pre-seed (Phase 18 D-07)
        // ensures an InProcess client is present; assert the picker's data
        // source is reachable + populated end-to-end.
        var resp = await Page.APIRequest.GetAsync(
            $"{RootUri}/api/v5/downloadclient",
            new APIRequestContextOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["X-Api-Key"] = ApiKey
                }
            });

        // STATE assertion 1: endpoint reachable, 2xx.
        resp.Status.Should().Be(
            200,
            "GET /api/v5/downloadclient must return 200 (the SelectDownloadClientModal data source)");

        // STATE assertion 2: response body carries the seeded InProcess client.
        var body = await resp.TextAsync();
        body.Should().NotBeNullOrWhiteSpace(
            "downloadclient listing must include the seeded InProcess client");
        body.Should().Contain(
            "InProcessImageDownloadClient",
            "SelectDownloadClientModal picker must have the baseline InProcess client to render");

        // STATE assertion 3: response body carries the `name` field — the
        // canonical picker label per DownloadClientResource shape.
        body.Should().Contain(
            "\"name\"",
            "SelectDownloadClientModal picker rows need the `name` field for display");
    }
}
