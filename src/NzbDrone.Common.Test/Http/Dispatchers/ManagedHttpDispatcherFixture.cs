using System.Net.Http;
using FluentAssertions;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Common.Http.Dispatchers;
using NzbDrone.Common.Http.Proxy;
using NzbDrone.Test.Common;

namespace NzbDrone.Common.Test.Http.Dispatchers
{
    /// <summary>
    /// Regression coverage for CR-01 (Phase 1 review): when an HttpRequest carries an
    /// explicit User-Agent header, the dispatcher must REPLACE the default UA it
    /// pre-seeds on the HttpRequestMessage rather than appending to it. The bug was
    /// that <see cref="ManagedHttpDispatcher"/>'s AddRequestHeaders called
    /// <c>UserAgent.ParseAdd(header.Value)</c> without clearing first, producing a
    /// concatenated UA like <c>Mangarr/4.0 Mangarr/0.1</c> on outbound traffic and
    /// silently violating the D-13 honest-UA contract.
    /// </summary>
    [TestFixture]
    public class ManagedHttpDispatcherFixture : TestBase
    {
        private TestableManagedHttpDispatcher _subject;

        [SetUp]
        public void Setup()
        {
            // The dispatcher constructor calls cacheManager.GetCache<HttpClient>(...);
            // a Moq-of-ICacheManager returns null and NREs. Use the real CacheManager.
            _subject = new TestableManagedHttpDispatcher(
                Mocker.GetMock<IHttpProxySettingsProvider>().Object,
                Mocker.GetMock<ICreateManagedWebProxy>().Object,
                Mocker.GetMock<ICertificateValidationService>().Object,
                Mocker.GetMock<IUserAgentBuilder>().Object,
                new CacheManager(),
                LogManager.GetCurrentClassLogger());
        }

        [Test]
        public void user_agent_header_should_replace_previously_set_default_not_append()
        {
            // Arrange — pre-seed the request message with the Mangarr-default UA the way
            // GetResponseAsync does at line 52, then invoke AddRequestHeaders with an
            // explicit Mangarr UA. The post-condition is that ONLY the explicit value
            // survives; no Mangarr/* product entry remains.
            using var requestMessage = new HttpRequestMessage();
            requestMessage.Headers.UserAgent.ParseAdd("Mangarr/4.0.0");

            var headers = new HttpHeader();
            headers["User-Agent"] = "Mangarr/1.2";

            _subject.InvokeAddRequestHeaders(requestMessage, headers);

            // The exact toString form is "Mangarr/1.2" with no leading "Mangarr/..." segment.
            requestMessage.Headers.UserAgent.ToString().Should().Be("Mangarr/1.2");
            requestMessage.Headers.UserAgent.ToString().Should().NotContain("Mangarr");
        }

        [Test]
        public void user_agent_header_should_be_set_when_no_default_was_seeded()
        {
            // Defensive companion: verify the Clear() does not break the no-default case.
            using var requestMessage = new HttpRequestMessage();

            var headers = new HttpHeader();
            headers["User-Agent"] = "Mangarr/0.1";

            _subject.InvokeAddRequestHeaders(requestMessage, headers);

            requestMessage.Headers.UserAgent.ToString().Should().Be("Mangarr/0.1");
        }

        /// <summary>
        /// Test shim that exposes the protected virtual <c>AddRequestHeaders</c> for
        /// direct invocation. The dispatcher's full GetResponseAsync pipeline requires
        /// network plumbing we don't want to stand up for a header-shape regression.
        /// </summary>
        private sealed class TestableManagedHttpDispatcher : ManagedHttpDispatcher
        {
            public TestableManagedHttpDispatcher(
                IHttpProxySettingsProvider proxySettingsProvider,
                ICreateManagedWebProxy createManagedWebProxy,
                ICertificateValidationService certificateValidationService,
                IUserAgentBuilder userAgentBuilder,
                ICacheManager cacheManager,
                Logger logger)
                : base(proxySettingsProvider, createManagedWebProxy, certificateValidationService, userAgentBuilder, cacheManager, logger)
            {
            }

            public void InvokeAddRequestHeaders(HttpRequestMessage webRequest, HttpHeader headers)
            {
                AddRequestHeaders(webRequest, headers);
            }
        }
    }
}
