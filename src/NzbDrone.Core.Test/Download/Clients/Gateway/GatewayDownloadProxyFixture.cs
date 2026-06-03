using System;
using System.Net;
using System.Net.Http;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Download.Clients.Gateway;
using NzbDrone.Core.Download.Clients.Gateway.Responses;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.Clients.Gateway
{
    /// <summary>
    /// Wave 0 fixture for <see cref="GatewayDownloadProxy"/> (GWDL-04) — covers the TWO deliberate
    /// divergences from the SABnzbd <c>SabnzbdProxy</c> template
    /// (<c>origin/v5-develop:src/NzbDrone.Core/Download/Clients/Sabnzbd/SabnzbdProxy.cs</c>) plus the
    /// exception ladder. References the NOT-YET-BUILT <see cref="GatewayDownloadProxy"/> and compiles
    /// GREEN only after Task 2 (contract-first ordering).
    ///
    /// Coverage:
    /// - submit_400_with_jobId_null_returns_response (Pitfall 1 / DIVERGENCE 1): a 400 body parses
    ///   as <see cref="GatewaySubmitResponse"/> with <c>JobId == null</c> — NOT a thrown transport
    ///   error.
    /// - removeJob_404_does_not_throw (DIVERGENCE 2): a DELETE returning 404 is idempotent-success.
    /// - submit_401_throws_authentication / connection_refused_throws_unavailable: the ladder.
    /// </summary>
    [TestFixture]
    public class GatewayDownloadProxyFixture : CoreTest<GatewayDownloadProxy>
    {
        private GatewayDownloadClientSettings _settings;

        [SetUp]
        public void Setup()
        {
            _settings = new GatewayDownloadClientSettings
            {
                Host = "localhost",
                Port = 9191,
                ApiKey = "test-api-key"
            };
        }

        private void GivenPostResponse(string content, HttpStatusCode statusCode)
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Post(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), content, statusCode));
        }

        private void GivenPostThrows(Exception ex)
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Post(It.IsAny<HttpRequest>()))
                .Throws(ex);
        }

        private void GivenExecuteResponse(HttpStatusCode statusCode)
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), string.Empty, statusCode));
        }

        private GatewaySubmitRequest BuildSubmitRequest()
        {
            return new GatewaySubmitRequest
            {
                ReleaseHandle = "opaque-handle",
                SourceKey = "comix.to",
                OutputFormat = "cbz"
            };
        }

        [Test]
        public void submit_200_returns_jobId()
        {
            GivenPostResponse("{\"jobId\":\"abc123\",\"status\":\"queued\"}", HttpStatusCode.OK);

            var resp = Subject.Submit(BuildSubmitRequest(), _settings);

            resp.Should().NotBeNull();
            resp.JobId.Should().Be("abc123");
        }

        [Test]
        public void submit_400_with_jobId_null_returns_response_not_throws()
        {
            // DIVERGENCE 1 / Pitfall 1 (GWDL-04): a 400 carries a SubmitResponse body (NOT the Error
            // envelope). The proxy parses it and returns {JobId:null} — the CLIENT translates that to
            // DownloadClientRejectedReleaseException, the proxy MUST NOT throw a transport error.
            GivenPostResponse("{\"jobId\":null,\"message\":\"expired handle\"}", HttpStatusCode.BadRequest);

            GatewaySubmitResponse resp = null;
            Action act = () => resp = Subject.Submit(BuildSubmitRequest(), _settings);

            act.Should().NotThrow();
            resp.Should().NotBeNull();
            resp.JobId.Should().BeNull();
            resp.Message.Should().Be("expired handle");
        }

        [Test]
        public void submit_401_throws_authentication()
        {
            GivenPostResponse("{\"error\":{\"code\":\"auth\",\"message\":\"bad key\"}}", HttpStatusCode.Unauthorized);

            Action act = () => Subject.Submit(BuildSubmitRequest(), _settings);

            act.Should().Throw<DownloadClientAuthenticationException>();
        }

        [Test]
        public void submit_500_throws_download_client_exception()
        {
            GivenPostResponse("{\"error\":{\"code\":\"internal\"}}", HttpStatusCode.InternalServerError);

            Action act = () => Subject.Submit(BuildSubmitRequest(), _settings);

            act.Should().Throw<DownloadClientException>()
               .Which.Should().NotBeOfType<DownloadClientAuthenticationException>();
        }

        [Test]
        public void submit_connection_refused_throws_unavailable()
        {
            GivenPostThrows(new HttpRequestException("Connection refused"));

            Action act = () => Subject.Submit(BuildSubmitRequest(), _settings);

            act.Should().Throw<DownloadClientUnavailableException>();
        }

        [Test]
        public void removeJob_404_does_not_throw()
        {
            // DIVERGENCE 2 (GWDL-04): a DELETE /downloads/{jobId} returning 404 (already removed) is
            // success-idempotent — the monitor may call RemoveItem twice. SAB has no analog.
            GivenExecuteResponse(HttpStatusCode.NotFound);

            Action act = () => Subject.RemoveJob("missing-job", false, _settings);

            act.Should().NotThrow();
        }

        [Test]
        public void removeJob_204_does_not_throw()
        {
            GivenExecuteResponse(HttpStatusCode.NoContent);

            Action act = () => Subject.RemoveJob("job-1", true, _settings);

            act.Should().NotThrow();
        }

        [Test]
        public void removeJob_401_throws_authentication()
        {
            GivenExecuteResponse(HttpStatusCode.Unauthorized);

            Action act = () => Subject.RemoveJob("job-1", false, _settings);

            act.Should().Throw<DownloadClientAuthenticationException>();
        }

        [Test]
        public void getJobs_200_deserializes_list()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(),
                    "{\"jobs\":[{\"jobId\":\"j1\",\"title\":\"Ch 1\",\"status\":\"completed\",\"outputPath\":\"/data/ch1.cbz\"}]}",
                    HttpStatusCode.OK));

            var list = Subject.GetJobs(_settings);

            list.Should().NotBeNull();
            list.Jobs.Should().HaveCount(1);
            list.Jobs[0].JobId.Should().Be("j1");
            list.Jobs[0].Status.Should().Be("completed");
        }

        [Test]
        public void getStatus_200_deserializes()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(),
                    "{\"isLocalhost\":true,\"outputRootFolders\":[\"/data/downloads\"]}",
                    HttpStatusCode.OK));

            var status = Subject.GetStatus(_settings);

            status.IsLocalhost.Should().BeTrue();
            status.OutputRootFolders.Should().ContainSingle().Which.Should().Be("/data/downloads");
        }

        [Test]
        public void getVersion_200_returns_version_string()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(c => c.Get(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(),
                    "{\"version\":\"1.0.0\",\"status\":\"ok\"}", HttpStatusCode.OK));

            var version = Subject.GetVersion(_settings);

            version.Should().Be("1.0.0");
        }
    }
}
