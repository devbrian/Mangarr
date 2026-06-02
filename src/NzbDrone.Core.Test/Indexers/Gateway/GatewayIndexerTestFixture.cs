using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using NzbDrone.Core.Indexers.Exceptions;
using NzbDrone.Core.Indexers.Gateway;
using NzbDrone.Core.Indexers.Gateway.Responses;
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
                Name = "Manga Gateway",
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

            // The failure message NAMES the unsearchable selected sources (D-04 actionable error).
            string.Join(" ", result.Errors.Select(e => e.ErrorMessage))
                  .Should().Contain("disabled.example");
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
