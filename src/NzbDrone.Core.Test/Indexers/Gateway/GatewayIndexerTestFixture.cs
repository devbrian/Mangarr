using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Exceptions;
using NzbDrone.Core.Indexers.Gateway;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.Gateway
{
    /// <summary>
    /// Wave 0 fixture for <see cref="GatewayIndexer.Test"/> — the GWIX-01 / D-04 connectivity +
    /// zero-searchable-source gate (lands in Plan 04 Task 2). <c>Test()</c> HARD-FAILS (blocks save)
    /// with an actionable, source-naming message when the selection resolves to zero
    /// enabled+searchable sources, and forces a live caps refetch (D-01). Same SetUp shape as
    /// <see cref="GatewayIndexerFixture"/>.
    /// </summary>
    [TestFixture]
    public class GatewayIndexerTestFixture : CoreTest<GatewayIndexer>
    {
        private GatewayCapabilities _capabilities;

        [SetUp]
        public void Setup()
        {
            Subject.Definition = new IndexerDefinition
            {
                Id = 99,
                Name = "Mangarr Gateway",
                Settings = new GatewaySettings
                {
                    BaseUrl = "http://localhost:9191",
                    ApiKey = "k"
                }
            };

            _capabilities = JsonConvert.DeserializeObject<GatewayCapabilities>(
                File.ReadAllText("Files/Indexers/Gateway/caps.json"));

            Mocker.GetMock<IGatewayCapabilitiesProvider>()
                  .Setup(p => p.GetCapabilities(It.IsAny<GatewaySettings>(), It.IsAny<bool>()))
                  .Returns(_capabilities);

            // The D-04 message embeds the {sources} token — echo the resolved token back so the
            // "message names the unsearchable sources" assertion exercises real behavior (the
            // production GatewayValidationNoSearchableSources value is "...: {sources}").
            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
                  .Returns<string, Dictionary<string, object>>((key, tokens) =>
                      tokens != null && tokens.TryGetValue("sources", out var sources)
                          ? $"{key}: {sources}"
                          : key);

            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(It.IsAny<string>()))
                  .Returns<string>(key => key);
        }

        [Test]
        public void Test_passes_with_searchable_source()
        {
            // Empty EnabledSources = all (D-04). caps.json has comix.to (enabled+searchable) +
            // mangadex (enabled+recent), so the effective set is non-empty → Test passes.
            var result = Subject.Test();

            result.IsValid.Should().BeTrue();
        }

        [Test]
        public void Test_passes_with_explicit_searchable_selection()
        {
            ((GatewaySettings)Subject.Definition.Settings).EnabledSources = new[] { "comix.to" };

            var result = Subject.Test();

            result.IsValid.Should().BeTrue();
        }

        [Test]
        public void Test_fails_when_zero_searchable_selected()
        {
            // disabled.example is enabled:false in caps.json — selecting ONLY it resolves to zero
            // enabled+searchable sources → D-04 HARD-FAIL.
            ((GatewaySettings)Subject.Definition.Settings).EnabledSources = new[] { "disabled.example" };

            var result = Subject.Test();

            result.IsValid.Should().BeFalse();

            // The failure message NAMES the unsearchable selected sources using their /caps DISPLAY
            // NAME, not the internal key (CodeRabbit minor): disabled.example → "Disabled Example".
            string.Join(" ", result.Errors.Select(e => e.ErrorMessage))
                  .Should().Contain("Disabled Example");
        }

        [Test]
        public void Test_fails_when_search_enabled_but_only_recent_capable_source_selected()
        {
            // CodeRabbit Major: mangadex in caps.json is supportsSearch=false / supportsRecent=true.
            // With a search feature enabled, selecting ONLY mangadex previously PASSED Test() (the old
            // OR predicate) yet BuildSearchChain (SupportsSearch) returns an empty chain → every
            // interactive/automatic search silently returns nothing. Test() must now hard-fail.
            ((IndexerDefinition)Subject.Definition).EnableAutomaticSearch = true;
            ((GatewaySettings)Subject.Definition.Settings).EnabledSources = new[] { "mangadex" };

            var result = Subject.Test();

            result.IsValid.Should().BeFalse();

            // Message names the offending source by display name (F5).
            string.Join(" ", result.Errors.Select(e => e.ErrorMessage))
                  .Should().Contain("MangaDex");
        }

        [Test]
        public void Test_passes_for_rss_only_with_a_recent_capable_source()
        {
            // The mirror of the above: with ONLY RSS enabled, a recent-capable source (mangadex)
            // is valid — GetRecentRequests filters on SupportsRecent, which mangadex satisfies.
            ((IndexerDefinition)Subject.Definition).EnableRss = true;
            ((IndexerDefinition)Subject.Definition).EnableAutomaticSearch = false;
            ((IndexerDefinition)Subject.Definition).EnableInteractiveSearch = false;
            ((GatewaySettings)Subject.Definition.Settings).EnabledSources = new[] { "mangadex" };

            var result = Subject.Test();

            result.IsValid.Should().BeTrue();
        }

        [Test]
        public void Test_capability_failure_with_empty_selection_names_effective_sources()
        {
            // CodeRabbit minor follow-up: with an EMPTY selection (= all enabled) and a gateway
            // whose only enabled source is recent-only, a search feature must fail NAMING that
            // source — not the misleading "(none enabled on the gateway)" placeholder.
            Mocker.GetMock<IGatewayCapabilitiesProvider>()
                  .Setup(p => p.GetCapabilities(It.IsAny<GatewaySettings>(), It.IsAny<bool>()))
                  .Returns(new GatewayCapabilities
                  {
                      Sources = new List<GatewaySourceCap>
                      {
                          new GatewaySourceCap
                          {
                              Key = "rss-only",
                              Name = "RSS Only Source",
                              Enabled = true,
                              SupportsSearch = false,
                              SupportsRecent = true
                          }
                      }
                  });
            ((IndexerDefinition)Subject.Definition).EnableAutomaticSearch = true;

            var result = Subject.Test();

            result.IsValid.Should().BeFalse();

            var message = string.Join(" ", result.Errors.Select(e => e.ErrorMessage));
            message.Should().Contain("RSS Only Source");
            message.Should().NotContain("none enabled");
        }

        [Test]
        public void Test_forces_caps_refetch()
        {
            // D-01: Test() always bypasses the cache.
            Subject.Test();

            Mocker.GetMock<IGatewayCapabilitiesProvider>()
                  .Verify(p => p.GetCapabilities(It.IsAny<GatewaySettings>(), true), Times.AtLeastOnce());
        }

        [Test]
        public void Test_fails_on_unreachable_gateway()
        {
            Mocker.GetMock<IGatewayCapabilitiesProvider>()
                  .Setup(p => p.GetCapabilities(It.IsAny<GatewaySettings>(), It.IsAny<bool>()))
                  .Throws(new IndexerException(null, "boom"));

            var result = Subject.Test();

            result.IsValid.Should().BeFalse();
        }

        [Test]
        public void Test_fails_with_apikey_error_on_invalid_key()
        {
            Mocker.GetMock<IGatewayCapabilitiesProvider>()
                  .Setup(p => p.GetCapabilities(It.IsAny<GatewaySettings>(), It.IsAny<bool>()))
                  .Throws(new ApiKeyException("auth failed"));

            var result = Subject.Test();

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "ApiKey");
        }
    }
}
