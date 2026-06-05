using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers.Gateway;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.Gateway
{
    /// <summary>
    /// Wave 0 fixture for <see cref="GatewaySettingsValidator"/> (Plan 37-01 Task 1). Locks the
    /// settings-validation contract: a valid root <c>BaseUrl</c> (security V5) AND a non-empty
    /// <c>ApiKey</c> (the gateway requires <c>X-Api-Key</c>). Instantiates
    /// <see cref="GatewaySettings"/> directly — no service mocks needed.
    /// </summary>
    [TestFixture]
    public class GatewaySettingsValidatorFixture : CoreTest
    {
        private static GatewaySettings ValidSettings()
        {
            return new GatewaySettings
            {
                BaseUrl = "http://localhost:9191",
                ApiKey = "test-api-key"
            };
        }

        [Test]
        public void validate_requires_apikey()
        {
            var settings = ValidSettings();
            settings.ApiKey = string.Empty;

            settings.Validate().IsValid.Should().BeFalse();
        }

        [TestCase("not-a-url")]
        [TestCase("ftp://localhost:9191")]
        [TestCase("localhost:9191")]
        public void validate_requires_valid_root_url(string badUrl)
        {
            var settings = ValidSettings();

            // ValidRootUrl() requires a parseable URL that starts with http(s)://.
            settings.BaseUrl = badUrl;

            settings.Validate().IsValid.Should().BeFalse();
        }

        [Test]
        public void valid_settings_pass()
        {
            ValidSettings().Validate().IsValid.Should().BeTrue();
        }

        [Test]
        public void result_limit_null_is_valid()
        {
            // Blank (null) = use the gateway's advertised default page size.
            var settings = ValidSettings();
            settings.ResultLimit = null;

            settings.Validate().IsValid.Should().BeTrue();
        }

        [TestCase(0)]
        [TestCase(-5)]
        public void result_limit_must_be_positive_when_provided(int badLimit)
        {
            var settings = ValidSettings();
            settings.ResultLimit = badLimit;

            settings.Validate().IsValid.Should().BeFalse();
        }

        [Test]
        public void result_limit_positive_is_valid()
        {
            var settings = ValidSettings();
            settings.ResultLimit = 250;

            settings.Validate().IsValid.Should().BeTrue();
        }
    }
}
