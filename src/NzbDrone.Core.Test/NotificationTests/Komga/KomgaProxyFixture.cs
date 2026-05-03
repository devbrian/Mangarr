using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Notifications.Komga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.NotificationTests.Komga
{
    [TestFixture]
    public class KomgaProxyFixture : CoreTest<KomgaProxy>
    {
        private KomgaNotificationSettings _settings;
        private HttpRequest _capturedRequest;

        [SetUp]
        public void Setup()
        {
            _settings = new KomgaNotificationSettings
            {
                Url = "http://komga.local:25600",
                ApiKey = "secret-api-key",
                LibraryId = 7
            };

            _capturedRequest = null;

            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                  .Callback<HttpRequest>(r => _capturedRequest = r)
                  .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), "[]", HttpStatusCode.OK));
        }

        [Test]
        public void scan_should_post_to_per_library_scan_endpoint()
        {
            Subject.Scan(_settings);

            _capturedRequest.Should().NotBeNull();
            _capturedRequest.Url.FullUri.Should().Contain("/api/v1/libraries/7/scan");
            _capturedRequest.Method.Should().Be(HttpMethod.Post);
        }

        [Test]
        public void scan_should_send_x_api_key_header()
        {
            Subject.Scan(_settings);

            _capturedRequest.Should().NotBeNull();
            _capturedRequest.Headers.Should().Contain(h => h.Key == "X-API-Key" && h.Value == "secret-api-key");
        }

        [Test]
        public void getlibraries_should_get_libraries_endpoint()
        {
            var libraries = new List<KomgaLibrary>
            {
                new() { Id = 7, Name = "Manga", Root = "/data/manga" }
            };

            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                  .Callback<HttpRequest>(r => _capturedRequest = r)
                  .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), libraries.ToJson(), HttpStatusCode.OK));

            var result = Subject.GetLibraries(_settings);

            _capturedRequest.Should().NotBeNull();
            _capturedRequest.Url.FullUri.Should().Contain("/api/v1/libraries");
            _capturedRequest.Url.FullUri.Should().NotContain("/scan");
            _capturedRequest.Method.Should().Be(HttpMethod.Get);
            result.Should().HaveCount(1);
            result[0].Id.Should().Be(7);
            result[0].Name.Should().Be("Manga");
        }

        [Test]
        public void getlibraries_should_send_x_api_key_header()
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                  .Callback<HttpRequest>(r => _capturedRequest = r)
                  .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), "[]", HttpStatusCode.OK));

            Subject.GetLibraries(_settings);

            _capturedRequest.Headers.Should().Contain(h => h.Key == "X-API-Key" && h.Value == "secret-api-key");
        }

        [Test]
        public void scan_should_handle_trailing_slash_in_url()
        {
            _settings.Url = "http://komga.local:25600/";

            Subject.Scan(_settings);

            _capturedRequest.Url.FullUri.Should().Contain("/api/v1/libraries/7/scan");
            _capturedRequest.Url.FullUri.Should().NotContain("//api");
        }
    }
}
