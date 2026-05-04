using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Notifications.Kavita;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.NotificationTests.Kavita
{
    [TestFixture]
    public class KavitaNotificationSettingsFixture : CoreTest
    {
        private KavitaNotificationSettings BuildSettings(int? libraryId = null)
        {
            return new KavitaNotificationSettings
            {
                Url = "http://kavita.local:5000",
                ApiKey = "abc123",
                LibraryId = libraryId
            };
        }

        [Test]
        public void should_be_valid_when_url_and_apikey_present_and_libraryid_null()
        {
            // D-16: Kavita HAS scan-all (unlike Komga); LibraryId is OPTIONAL.
            var settings = BuildSettings(libraryId: null);

            var result = settings.Validate();

            result.IsValid.Should().BeTrue();
        }

        [Test]
        public void should_be_valid_when_libraryid_is_positive()
        {
            var settings = BuildSettings(libraryId: 7);

            var result = settings.Validate();

            result.IsValid.Should().BeTrue();
        }

        [Test]
        public void should_be_invalid_when_libraryid_is_zero()
        {
            // D-16: explicit 0/negative LibraryId is a misconfiguration; only null (= scan-all) is valid.
            var settings = BuildSettings(libraryId: 0);

            var result = settings.Validate();

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName.Contains("LibraryId"));
        }

        [Test]
        public void should_be_invalid_when_libraryid_is_negative()
        {
            var settings = BuildSettings(libraryId: -1);

            var result = settings.Validate();

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName.Contains("LibraryId"));
        }

        [Test]
        public void should_be_invalid_when_apikey_is_empty()
        {
            var settings = BuildSettings();
            settings.ApiKey = string.Empty;

            var result = settings.Validate();

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "ApiKey");
        }

        [Test]
        public void should_be_invalid_when_url_is_not_http_or_https()
        {
            var settings = BuildSettings();
            settings.Url = "ftp://kavita.local";

            var result = settings.Validate();

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == "Url");
        }
    }
}
