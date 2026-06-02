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

        [Test]
        public void validate_requires_valid_root_url()
        {
            var settings = ValidSettings();

            // A non-root URL (path segment present) fails ValidRootUrl().
            settings.BaseUrl = "http://localhost:9191/some/path";

            settings.Validate().IsValid.Should().BeFalse();
        }

        [Test]
        public void valid_settings_pass()
        {
            ValidSettings().Validate().IsValid.Should().BeTrue();
        }
    }
}
