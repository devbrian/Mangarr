using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Notifications.Kavita;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.NotificationTests.Kavita
{
    [TestFixture]
    public class KavitaProxyFixture : CoreTest<KavitaProxy>
    {
        private KavitaNotificationSettings _settings;
        private List<HttpRequest> _capturedRequests;
        private Queue<Func<HttpRequest, HttpResponse>> _responses;

        [SetUp]
        public void Setup()
        {
            // TestBase.Mocker pre-registers a real CacheManager — no SetConstant needed.
            _settings = new KavitaNotificationSettings
            {
                Url = "http://kavita.local:5000",
                ApiKey = "user-api-key",
                LibraryId = null
            };

            _capturedRequests = new List<HttpRequest>();
            _responses = new Queue<Func<HttpRequest, HttpResponse>>();

            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                  .Callback<HttpRequest>(r => _capturedRequests.Add(r))
                  .Returns<HttpRequest>(r => _responses.Dequeue()(r));
        }

        private static HttpResponse JsonOk(HttpRequest r, string content)
        {
            return new HttpResponse(r, new HttpHeader(), content, HttpStatusCode.OK);
        }

        private static HttpResponse Unauthorized(HttpRequest r)
        {
            return new HttpResponse(r, new HttpHeader(), string.Empty, HttpStatusCode.Unauthorized);
        }

        private static string AuthBody(string token)
        {
            return new KavitaAuthResponse { Token = token, Username = "mangarr" }.ToJson();
        }

        [Test]
        public void scan_should_authenticate_first_then_dispatch_scan()
        {
            _responses.Enqueue(r => JsonOk(r, AuthBody("jwt-1")));
            _responses.Enqueue(r => JsonOk(r, "{}"));

            Subject.Scan(_settings);

            _capturedRequests.Should().HaveCount(2);
            _capturedRequests[0].Url.FullUri.Should().Contain("/api/Plugin/authenticate");
            _capturedRequests[0].Url.FullUri.Should().Contain("apiKey=user-api-key");
            _capturedRequests[0].Url.FullUri.Should().Contain("pluginName=Mangarr");
            _capturedRequests[0].Method.Should().Be(HttpMethod.Post);

            _capturedRequests[1].Url.FullUri.Should().Contain("/api/Library/scan-all");
            _capturedRequests[1].Method.Should().Be(HttpMethod.Post);
            _capturedRequests[1].Headers.Should().Contain(h => h.Key == "Authorization" && h.Value == "Bearer jwt-1");
        }

        [Test]
        public void second_scan_should_reuse_cached_jwt_no_second_auth_call()
        {
            _responses.Enqueue(r => JsonOk(r, AuthBody("jwt-1")));
            _responses.Enqueue(r => JsonOk(r, "{}"));
            _responses.Enqueue(r => JsonOk(r, "{}"));

            Subject.Scan(_settings);
            Subject.Scan(_settings);

            // 1 auth + 2 scans = 3 total HTTP calls (NOT 2 auths + 2 scans = 4).
            _capturedRequests.Should().HaveCount(3);
            _capturedRequests[0].Url.FullUri.Should().Contain("/api/Plugin/authenticate");
            _capturedRequests[1].Url.FullUri.Should().Contain("/api/Library/scan-all");
            _capturedRequests[2].Url.FullUri.Should().Contain("/api/Library/scan-all");
            _capturedRequests[1].Headers.Should().Contain(h => h.Key == "Authorization" && h.Value == "Bearer jwt-1");
            _capturedRequests[2].Headers.Should().Contain(h => h.Key == "Authorization" && h.Value == "Bearer jwt-1");
        }

        [Test]
        public void scan_should_invalidate_cache_and_retry_once_on_401()
        {
            // Sequence: auth -> scan(401) -> auth(refresh) -> scan(200).
            _responses.Enqueue(r => JsonOk(r, AuthBody("jwt-stale")));
            _responses.Enqueue(r => Unauthorized(r));
            _responses.Enqueue(r => JsonOk(r, AuthBody("jwt-fresh")));
            _responses.Enqueue(r => JsonOk(r, "{}"));

            // The proxy lets the second 401 propagate; the first one triggers reauth+retry.
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                  .Callback<HttpRequest>(r => _capturedRequests.Add(r))
                  .Returns<HttpRequest>(r =>
                  {
                      var fn = _responses.Dequeue();
                      var resp = fn(r);
                      if (resp.StatusCode == HttpStatusCode.Unauthorized)
                      {
                          throw new HttpException(r, resp);
                      }

                      return resp;
                  });

            Subject.Scan(_settings);

            _capturedRequests.Should().HaveCount(4);
            _capturedRequests[0].Url.FullUri.Should().Contain("/api/Plugin/authenticate");
            _capturedRequests[1].Url.FullUri.Should().Contain("/api/Library/scan-all");
            _capturedRequests[1].Headers.Should().Contain(h => h.Key == "Authorization" && h.Value == "Bearer jwt-stale");
            _capturedRequests[2].Url.FullUri.Should().Contain("/api/Plugin/authenticate"); // refresh
            _capturedRequests[3].Url.FullUri.Should().Contain("/api/Library/scan-all");
            _capturedRequests[3].Headers.Should().Contain(h => h.Key == "Authorization" && h.Value == "Bearer jwt-fresh");
        }

        [Test]
        public void scan_should_rethrow_when_second_attempt_also_returns_401()
        {
            // Pattern 6 contract: ONE retry, NOT infinite loop. Second 401 propagates.
            _responses.Enqueue(r => JsonOk(r, AuthBody("jwt-stale")));
            _responses.Enqueue(r => Unauthorized(r));
            _responses.Enqueue(r => JsonOk(r, AuthBody("jwt-fresh")));
            _responses.Enqueue(r => Unauthorized(r));

            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                  .Callback<HttpRequest>(r => _capturedRequests.Add(r))
                  .Returns<HttpRequest>(r =>
                  {
                      var fn = _responses.Dequeue();
                      var resp = fn(r);
                      if (resp.StatusCode == HttpStatusCode.Unauthorized)
                      {
                          throw new HttpException(r, resp);
                      }

                      return resp;
                  });

            Action act = () => Subject.Scan(_settings);

            act.Should().Throw<HttpException>()
                .Where(e => e.Response.StatusCode == HttpStatusCode.Unauthorized);

            // Exactly 4 calls (auth, scan-401, auth, scan-401) — no third retry.
            _capturedRequests.Should().HaveCount(4);
        }

        [Test]
        public void scan_with_libraryid_should_post_to_per_library_endpoint()
        {
            _settings.LibraryId = 7;

            _responses.Enqueue(r => JsonOk(r, AuthBody("jwt-1")));
            _responses.Enqueue(r => JsonOk(r, "{}"));

            Subject.Scan(_settings);

            _capturedRequests[1].Url.FullUri.Should().Contain("/api/Library/scan?libraryId=7");
            _capturedRequests[1].Url.FullUri.Should().NotContain("scan-all");
        }

        [Test]
        public void scan_without_libraryid_should_post_to_scan_all_endpoint()
        {
            _settings.LibraryId = null;

            _responses.Enqueue(r => JsonOk(r, AuthBody("jwt-1")));
            _responses.Enqueue(r => JsonOk(r, "{}"));

            Subject.Scan(_settings);

            _capturedRequests[1].Url.FullUri.Should().Contain("/api/Library/scan-all");
            _capturedRequests[1].Url.FullUri.Should().NotContain("libraryId=");
        }

        [Test]
        public void cache_key_should_isolate_per_url_and_apikey()
        {
            // Two different settings (different ApiKey) — both should authenticate independently.
            _responses.Enqueue(r => JsonOk(r, AuthBody("jwt-A")));
            _responses.Enqueue(r => JsonOk(r, "{}"));
            _responses.Enqueue(r => JsonOk(r, AuthBody("jwt-B")));
            _responses.Enqueue(r => JsonOk(r, "{}"));

            var settingsA = _settings;
            var settingsB = new KavitaNotificationSettings
            {
                Url = _settings.Url,
                ApiKey = "different-api-key",
                LibraryId = null
            };

            Subject.Scan(settingsA);
            Subject.Scan(settingsB);

            // Two distinct (Url, ApiKey) pairs → two distinct auth calls.
            _capturedRequests.Should().HaveCount(4);
            _capturedRequests[1].Headers.Should().Contain(h => h.Key == "Authorization" && h.Value == "Bearer jwt-A");
            _capturedRequests[3].Headers.Should().Contain(h => h.Key == "Authorization" && h.Value == "Bearer jwt-B");
        }

        [Test]
        public void test_should_force_fresh_auth_each_call()
        {
            // Two Test() calls back-to-back must NOT share a cached token — Test is a connectivity probe.
            _responses.Enqueue(r => JsonOk(r, AuthBody("jwt-1")));
            _responses.Enqueue(r => JsonOk(r, AuthBody("jwt-2")));

            Subject.Test(_settings);
            Subject.Test(_settings);

            _capturedRequests.Should().HaveCount(2);
            _capturedRequests[0].Url.FullUri.Should().Contain("/api/Plugin/authenticate");
            _capturedRequests[1].Url.FullUri.Should().Contain("/api/Plugin/authenticate");
        }

        [Test]
        public void test_should_throw_when_authenticate_returns_no_token()
        {
            _responses.Enqueue(r => JsonOk(r, new KavitaAuthResponse { Token = string.Empty }.ToJson()));

            Action act = () => Subject.Test(_settings);

            act.Should().Throw<Exception>().WithMessage("*token*");
        }

        [Test]
        public void scan_should_handle_trailing_slash_in_url()
        {
            _settings.Url = "http://kavita.local:5000/";

            _responses.Enqueue(r => JsonOk(r, AuthBody("jwt-1")));
            _responses.Enqueue(r => JsonOk(r, "{}"));

            Subject.Scan(_settings);

            _capturedRequests[0].Url.FullUri.Should().NotContain("//api");
            _capturedRequests[1].Url.FullUri.Should().NotContain("//api");
        }
    }
}
