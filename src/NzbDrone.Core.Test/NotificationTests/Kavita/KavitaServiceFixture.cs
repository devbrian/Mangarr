using System;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Notifications.Kavita;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.NotificationTests.Kavita
{
    [TestFixture]
    public class KavitaServiceFixture : CoreTest<KavitaService>
    {
        private KavitaNotificationSettings _settings;

        [SetUp]
        public void Setup()
        {
            _settings = new KavitaNotificationSettings
            {
                Url = "http://kavita.local:5000",
                ApiKey = "test-key",
                LibraryId = null
            };
        }

        [Test]
        public void test_should_return_null_on_successful_authentication()
        {
            // No throw from proxy.Test => connectivity OK.
            var result = Subject.Test(_settings);

            result.Should().BeNull();
            Mocker.GetMock<IKavitaProxy>().Verify(p => p.Test(It.IsAny<KavitaNotificationSettings>()), Times.Once);
        }

        [Test]
        public void test_should_return_failure_when_proxy_throws()
        {
            Mocker.GetMock<IKavitaProxy>()
                  .Setup(p => p.Test(It.IsAny<KavitaNotificationSettings>()))
                  .Throws(new InvalidOperationException("connection refused"));

            var result = Subject.Test(_settings);

            result.Should().NotBeNull();
            result.PropertyName.Should().Be("Url");
            result.ErrorMessage.Should().Contain("connection refused");

            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
