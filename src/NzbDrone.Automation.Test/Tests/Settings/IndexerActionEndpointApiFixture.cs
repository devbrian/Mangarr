using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using RestSharp;

namespace NzbDrone.Automation.Test.Tests.Settings;

/// <summary>
/// GH #169 follow-up resolution / Phase 39 Plan 39-07 — extends Indexer action-endpoint
/// coverage beyond the gateway pick's UI footer surface, which exposes NO
/// providerAction-typed footer BUTTON (see IndexerActionButtonFixture.cs in-test
/// rationale + the GH #169 issue body). Per the GH #169 acceptance path, this fixture
/// closes the gap by exercising the <c>POST /api/v5/indexer/action/{name}</c> route at the
/// wire level via TestKit (no providerAction-bearing UI footer button required, no test-only
/// IIndexer registration).
///
/// Coverage rationale:
///   - <c>IndexerActionButtonFixture</c> (UI) asserts the static modal-footer
///     invariant: the gateway's EditIndexerModal renders zero non-canonical buttons,
///     so the user-visible action-button path is unreachable on the sole indexer
///     (documented gap).
///   - <c>IndexerActionButtonOfflineFixture</c> (UI/offline) observes the Test
///     button as the wire-level proxy on the sole indexer (documented
///     paired-offline obligation).
///   - <c>IndexerActionEndpointApiFixture</c> (this fixture, API-only) hits the
///     action endpoint family directly with an arbitrary (non-gatewaySources) action
///     name. The GatewayIndexer's RequestAction override handles only the
///     <c>gatewaySources</c> action and falls through to IndexerBase.RequestAction's
///     default (returns null) for any other name, so the controller wraps null in a
///     ContentHttpResult with HTTP 200 + body "null". That deterministic round-trip
///     proves the controller's dispatch + ProviderControllerBase.RequestAction routing
///     path is wired end-to-end. Phase 39 Plan 39-07 repointed this from the retired
///     in-process MangaDex indexer to the GatewayIndexer.
///
/// Tier: PRSmoke (no live upstream wait; pure local controller round-trip).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
[Category("PRSmoke")]
public class IndexerActionEndpointApiFixture : AutomationTest
{
    private int _indexerId;

    [OneTimeSetUp]
    public async Task SeedAsync()
    {
        var tk = new TestKit.TestKit(RootUri, ApiKey, string.Empty);
        _indexerId = await tk.SeedIndexerAsync();
    }

    [Test]
    public async Task action_endpoint_dispatches_for_seeded_gateway_indexer()
    {
        var tk = new TestKit.TestKit(RootUri, ApiKey, string.Empty);

        // Probe-name chosen to demonstrate the dispatch contract without invoking
        // any real provider action — the GatewayIndexer handles only the
        // "gatewaySources" action and falls through to IndexerBase.RequestAction's
        // default (returns null) for any other name, which ProviderControllerBase
        // wraps in a 200 ContentHttpResult with body "null". A 200/"null" round-trip
        // is the deterministic proof that the controller route + body deserialization
        // + ProviderFactory.RequestAction dispatch all wired correctly.
        var response = await tk.RequestIndexerActionAsync(_indexerId, "probe-action-coverage-fixture");

        response.ResponseStatus.Should().Be(ResponseStatus.Completed,
            "API request must complete (no transport failure); body=" + response.Content);
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the gateway falls through to IndexerBase.RequestAction default → ContentHttpResult(\"null\", 200); body=" + response.Content);

        // Body is the JSON-serialized return of indexer.RequestAction(name, query).
        // For the gateway probed with a non-gatewaySources name (default IndexerBase
        // override returns null), the .ToJson() wraps the null in the literal string "null".
        response.Content.Should().Be("null",
            "Expected literal 'null' (JSON of IndexerBase.RequestAction default return); got: " + response.Content);
    }
}
