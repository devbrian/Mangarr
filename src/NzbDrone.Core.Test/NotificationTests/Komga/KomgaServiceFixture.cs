using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Notifications.Komga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.NotificationTests.Komga
{
    [TestFixture]
    public class KomgaServiceFixture : CoreTest<KomgaService>
    {
        private KomgaNotificationSettings _settings;

        [SetUp]
        public void Setup()
        {
            _settings = new KomgaNotificationSettings
            {
                Url = "http://komga.local:25600",
                ApiKey = "test-key",
                LibraryId = 7
            };
        }

        [Test]
        public void test_should_return_null_on_successful_get_libraries()
        {
            Mocker.GetMock<IKomgaProxy>()
                  .Setup(p => p.GetLibraries(It.IsAny<KomgaNotificationSettings>()))
                  .Returns(new List<KomgaLibrary> { new() { Id = 7, Name = "Manga" } });

            var result = Subject.Test(_settings);

            result.Should().BeNull();
        }

        [Test]
        public void test_should_return_failure_when_proxy_throws()
        {
            Mocker.GetMock<IKomgaProxy>()
                  .Setup(p => p.GetLibraries(It.IsAny<KomgaNotificationSettings>()))
                  .Throws(new InvalidOperationException("connection refused"));

            var result = Subject.Test(_settings);

            result.Should().NotBeNull();
            result.PropertyName.Should().Be("Url");
            result.ErrorMessage.Should().Contain("connection refused");
        }

        [Test]
        public void test_should_return_failure_when_proxy_returns_null()
        {
            Mocker.GetMock<IKomgaProxy>()
                  .Setup(p => p.GetLibraries(It.IsAny<KomgaNotificationSettings>()))
                  .Returns((List<KomgaLibrary>)null);

            var result = Subject.Test(_settings);

            result.Should().NotBeNull();
            result.PropertyName.Should().Be("Url");
        }
    }
}
