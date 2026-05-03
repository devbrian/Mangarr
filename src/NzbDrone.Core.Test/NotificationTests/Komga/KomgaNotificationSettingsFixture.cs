using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Notifications.Komga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.NotificationTests.Komga
{
    [TestFixture]
    public class KomgaNotificationSettingsFixture : CoreTest
    {
        private KomgaNotificationSettings BuildSettings(int? libraryId = 1)
        {
            return new KomgaNotificationSettings
            {
                Url = "http://komga.local:25600",
                ApiKey = "abc123",
                LibraryId = libraryId
            };
        }

        [Test]
        public void should_be_valid_when_url_apikey_and_libraryid_are_present()
        {
            var settings = BuildSettings(libraryId: 1);

            var result = settings.Validate();

            result.IsValid.Should().BeTrue();
        }

        [Test]
        public void should_be_invalid_when_libraryid_is_null()
        {
            // Pitfall 2: Komga has NO scan-all endpoint; LibraryId is REQUIRED.
            var settings = BuildSettings(libraryId: null);

            var result = settings.Validate();

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "LibraryId");
        }

        [Test]
        public void should_be_invalid_when_libraryid_is_zero()
        {
            // Pitfall 2 mitigation: validator enforces GreaterThan(0).
            var settings = BuildSettings(libraryId: 0);

            var result = settings.Validate();

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "LibraryId");
        }

        [Test]
        public void should_be_invalid_when_apikey_is_empty()
        {
            var settings = BuildSettings(libraryId: 1);
            settings.ApiKey = string.Empty;

            var result = settings.Validate();

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "ApiKey");
        }

        [Test]
        public void should_be_invalid_when_url_is_not_http_or_https()
        {
            var settings = BuildSettings(libraryId: 1);
            settings.Url = "ftp://komga.local";

            var result = settings.Validate();

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "Url");
        }
    }
}
