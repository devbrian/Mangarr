using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// Phase 20 Plan 20-04 D-08 paired-offline companion for IndexerTestAllFixture
/// (seeds/liveservice-coverage-policy.md §3 verbatim).
///
/// Asserts:
///   (a) the Test-All toolbar button renders on /settings/indexers (form-render)
///   (b) clicking it wires to POST /api/v5/indexer/testall (button-wiring)
///   (c) the outbound request shape matches the cataloged endpoint
///       (URL + method captured via Page.Request)
///
/// PRSmoke tier — fast (no live upstream wait, capture-and-go). Runs against
/// the local Mangarr backend; the Mangarr-to-upstream hop is what would
/// require a cassette in offline mode, but this fixture only observes the
/// browser-to-Mangarr surface so cassette mode is orthogonal.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class IndexerTestAllOfflineFixture : AutomationTest
{
    [OneTimeSetUp]
    public async Task SeedAsync()
    {
        var tk = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
        await tk.SeedIndexerAsync();
    }

    [Test]
    public async Task loads_offline_testall_shape()
    {
        var settings = await new SettingsIndexersPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(settings.PageContainer).ToBeVisibleAsync();

        // (a) form-render
        await Assertions.Expect(settings.TestAllButton).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // (b)+(c) button-wiring + request-shape — race the click against the
        // outbound request capture. WaitForRequestAsync returns the IRequest
        // synchronously when the matching pattern fires; no live response
        // hop is needed for the contract assertion.
        var captureTask = Page.WaitForRequestAsync(
            r => r.Url.Contains("/api/v5/indexer/testall") && r.Method == "POST",
            new() { Timeout = 30_000 });

        await settings.TestAllButton.ClickAsync();
        var capturedRequest = await captureTask;

        capturedRequest.Method.Should().Be("POST");
        capturedRequest.Url.Should().Contain("/api/v5/indexer/testall");
    }
}
