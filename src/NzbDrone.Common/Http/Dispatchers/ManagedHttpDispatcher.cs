using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http.HappyEyeballs;
using NzbDrone.Common.Http.Proxy;

namespace NzbDrone.Common.Http.Dispatchers
{
    public class ManagedHttpDispatcher : IHttpDispatcher
    {
        private const string NO_PROXY_KEY = "no-proxy";

        private readonly IHttpProxySettingsProvider _proxySettingsProvider;
        private readonly ICreateManagedWebProxy _createManagedWebProxy;
        private readonly ICertificateValidationService _certificateValidationService;
        private readonly IUserAgentBuilder _userAgentBuilder;
        private readonly ICached<System.Net.Http.HttpClient> _httpClientCache;
        private readonly ICached<CredentialCache> _credentialCache;
        private readonly HttpHappyEyeballs _httpHappyEyeballs;

        public ManagedHttpDispatcher(IHttpProxySettingsProvider proxySettingsProvider,
            ICreateManagedWebProxy createManagedWebProxy,
            ICertificateValidationService certificateValidationService,
            IUserAgentBuilder userAgentBuilder,
            ICacheManager cacheManager,
            Logger logger)
        {
            _proxySettingsProvider = proxySettingsProvider;
            _createManagedWebProxy = createManagedWebProxy;
            _certificateValidationService = certificateValidationService;
            _userAgentBuilder = userAgentBuilder;

            _httpClientCache = cacheManager.GetCache<System.Net.Http.HttpClient>(typeof(ManagedHttpDispatcher));
            _credentialCache = cacheManager.GetCache<CredentialCache>(typeof(ManagedHttpDispatcher), "credentialcache");

            _httpHappyEyeballs = new HttpHappyEyeballs(logger);
        }

        public async Task<HttpResponse> GetResponseAsync(HttpRequest request, CookieContainer cookies)
        {
            using var requestMessage = new HttpRequestMessage(request.Method, (Uri)request.Url);
            requestMessage.Version = HttpVersion.Version20;
            requestMessage.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            requestMessage.Headers.UserAgent.ParseAdd(_userAgentBuilder.GetUserAgent(request.UseSimplifiedUserAgent));
            requestMessage.Headers.ConnectionClose = !request.ConnectionKeepAlive;

            var cookieHeader = cookies.GetCookieHeader((Uri)request.Url);
            if (cookieHeader.IsNotNullOrWhiteSpace())
            {
                requestMessage.Headers.Add("Cookie", cookieHeader);
            }

            using var cts = new CancellationTokenSource();
            if (request.RequestTimeout != TimeSpan.Zero)
            {
                cts.CancelAfter(request.RequestTimeout);
            }
            else
            {
                // The default for System.Net.Http.HttpClient
                cts.CancelAfter(TimeSpan.FromSeconds(100));
            }

            if (request.Credentials != null)
            {
                if (request.Credentials is BasicNetworkCredential bc)
                {
                    // Manually set header to avoid initial challenge response
                    var authInfo = bc.UserName + ":" + bc.Password;
                    authInfo = Convert.ToBase64String(Encoding.GetEncoding("ISO-8859-1").GetBytes(authInfo));
                    requestMessage.Headers.Add("Authorization", "Basic " + authInfo);
                }
                else if (request.Credentials is NetworkCredential nc)
                {
                    var creds = GetCredentialCache();
                    foreach (var authtype in new[] { "Basic", "Digest" })
                    {
                        creds.Remove((Uri)request.Url, authtype);
                        creds.Add((Uri)request.Url, authtype, nc);
                    }
                }
            }

            if (request.ContentData != null)
            {
                requestMessage.Content = new ByteArrayContent(request.ContentData);
            }

            if (request.Headers != null)
            {
                AddRequestHeaders(requestMessage, request.Headers);
            }

            var httpClient = GetClient(request.Url);

            try
            {
                using var responseMessage = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                byte[] data = null;

                try
                {
                    if (request.ResponseStream != null && responseMessage.StatusCode == HttpStatusCode.OK)
                    {
                        await responseMessage.Content.CopyToAsync(request.ResponseStream, null, cts.Token);
                    }
                    else
                    {
                        data = await responseMessage.Content.ReadAsByteArrayAsync(cts.Token);
                    }
                }
                catch (Exception ex)
                {
                    throw new WebException("Failed to read complete http response", ex, WebExceptionStatus.ReceiveFailure, null);
                }

                var headers = responseMessage.Headers.ToNameValueCollection();

                headers.Add(responseMessage.Content.Headers.ToNameValueCollection());

                return new HttpResponse(request, new HttpHeader(headers), data, responseMessage.StatusCode, responseMessage.Version);
            }
            catch (OperationCanceledException ex) when (cts.IsCancellationRequested)
            {
                throw new WebException("Http request timed out", ex, WebExceptionStatus.Timeout, null);
            }
        }

        protected virtual System.Net.Http.HttpClient GetClient(HttpUri uri)
        {
            var proxySettings = _proxySettingsProvider.GetProxySettings(uri);

            var key = proxySettings?.Key ?? NO_PROXY_KEY;

            return _httpClientCache.Get(key, () => CreateHttpClient(proxySettings));
        }

        protected virtual System.Net.Http.HttpClient CreateHttpClient(HttpProxySettings proxySettings)
        {
            var handler = new SocketsHttpHandler()
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Brotli,
                UseCookies = false, // sic - we don't want to use a shared cookie container
                AllowAutoRedirect = false,
                Credentials = GetCredentialCache(),
                PreAuthenticate = true,
                MaxConnectionsPerServer = 12,
                ConnectCallback = Socket.OSSupportsIPv6 ? _httpHappyEyeballs.OnConnect : null,
                SslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = _certificateValidationService.ShouldByPassValidationError
                }
            };

            if (proxySettings != null)
            {
                handler.Proxy = _createManagedWebProxy.GetWebProxy(proxySettings);
            }

            // PHASE 18 TEST HOOK — Automation cassette interceptor.
            // Activated only when MANGARR_TEST_CASSETTE_MODE env var is set (Replay|Record|ReplayOrRecord).
            // Production paths always have the env var unset, so this branch is dead code in prod.
            // Per 18-RESEARCH.md §"Cassette injection mechanism" + Phase 18 D-09.
            HttpMessageHandler effectiveHandler = handler;
            var cassetteMode = Environment.GetEnvironmentVariable("MANGARR_TEST_CASSETTE_MODE");
            var cassetteDir = Environment.GetEnvironmentVariable("MANGARR_TEST_CASSETTE_DIR");
            var sentinelPath = Environment.GetEnvironmentVariable("MANGARR_TEST_SENTINEL_PNG");
            if (!string.IsNullOrEmpty(cassetteMode) && !string.IsNullOrEmpty(cassetteDir))
            {
                // Reflection load to avoid taking a hard dep from Mangarr.Common on the test assembly.
                const string asmName = "Mangarr.Automation.Test";
                const string typeName = "NzbDrone.Automation.Test.TestKit.CassetteHandler";
                const string modeTypeName = "NzbDrone.Automation.Test.TestKit.CassetteMode";
                var asm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == asmName);

                // Cross-process fallback (18-14 D-B fix): the Mangarr backend is spawned by
                // NzbDroneRunner as a separate process whose AppDomain never loads the test
                // assembly. The test runner sets MANGARR_TEST_ASSEMBLY_PATH to the absolute
                // .dll path so the backend can Assembly.LoadFrom it on demand. Production
                // never sets this env var, so the branch is dead code outside test runs.
                if (asm == null)
                {
                    var asmPath = Environment.GetEnvironmentVariable("MANGARR_TEST_ASSEMBLY_PATH");
                    if (!string.IsNullOrEmpty(asmPath) && File.Exists(asmPath))
                    {
                        try
                        {
                            asm = System.Reflection.Assembly.LoadFrom(asmPath);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Trace.WriteLine(
                                $"MANGARR_TEST_ASSEMBLY_PATH='{asmPath}' load failed: {ex.Message}; falling through to production handler.");
                        }
                    }
                }

                if (asm != null)
                {
                    var type = asm.GetType(typeName);
                    var modeEnumType = asm.GetType(modeTypeName);
                    if (type != null && modeEnumType != null)
                    {
                        // BL-01 fix (18-13): Enum.Parse throws on a typo'd env-var value,
                        // crashing every production HTTP path. Use the reflection-friendly
                        // Enum.TryParse(Type, string, bool, out object) overload instead;
                        // on failure, log via Trace and fall through to the production
                        // handler. modeEnumType is loaded reflectively across an assembly
                        // boundary so we can't take a static dep on CassetteMode here.
                        var tryParseMethod = typeof(Enum).GetMethods()
                            .First(m => m.Name == "TryParse"
                                        && m.IsGenericMethodDefinition == false
                                        && m.GetParameters().Length == 4
                                        && m.GetParameters()[0].ParameterType == typeof(Type)
                                        && m.GetParameters()[1].ParameterType == typeof(string)
                                        && m.GetParameters()[2].ParameterType == typeof(bool)
                                        && m.GetParameters()[3].ParameterType.IsByRef);
                        var parseArgs = new object[] { modeEnumType, cassetteMode, true, null };
                        var parsed = (bool)tryParseMethod.Invoke(null, parseArgs);
                        if (parsed)
                        {
                            var modeValue = parseArgs[3];
                            effectiveHandler = (HttpMessageHandler)Activator.CreateInstance(
                                type, cassetteDir, modeValue, sentinelPath ?? string.Empty, handler);
                        }
                        else
                        {
                            System.Diagnostics.Trace.WriteLine(
                                $"MANGARR_TEST_CASSETTE_MODE='{cassetteMode}' is not a valid CassetteMode; ignoring.");
                        }
                    }
                }
            }

            var client = new System.Net.Http.HttpClient(effectiveHandler)
            {
                DefaultRequestVersion = HttpVersion.Version20,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
                Timeout = Timeout.InfiniteTimeSpan
            };

            return client;
        }

        protected virtual void AddRequestHeaders(HttpRequestMessage webRequest, HttpHeader headers)
        {
            foreach (var header in headers)
            {
                switch (header.Key)
                {
                    case "Accept":
                        webRequest.Headers.Accept.ParseAdd(header.Value);
                        break;
                    case "Connection":
                        webRequest.Headers.Connection.Clear();
                        webRequest.Headers.Connection.Add(header.Value);
                        break;
                    case "Content-Length":
                        AddContentHeader(webRequest, "Content-Length", header.Value);
                        break;
                    case "Content-Type":
                        AddContentHeader(webRequest, "Content-Type", header.Value);
                        break;
                    case "Date":
                        webRequest.Headers.Remove("Date");
                        webRequest.Headers.Date = HttpHeader.ParseDateTime(header.Value);
                        break;
                    case "Expect":
                        webRequest.Headers.Expect.ParseAdd(header.Value);
                        break;
                    case "Host":
                        webRequest.Headers.Host = header.Value;
                        break;
                    case "If-Modified-Since":
                        webRequest.Headers.IfModifiedSince = HttpHeader.ParseDateTime(header.Value);
                        break;
                    case "Range":
                        throw new NotImplementedException();
                    case "Referer":
                        webRequest.Headers.Add("Referer", header.Value);
                        break;
                    case "Transfer-Encoding":
                        webRequest.Headers.TransferEncoding.ParseAdd(header.Value);
                        break;
                    case "User-Agent":
                        // D-13 honest UA contract: an explicit User-Agent on the HttpRequest
                        // must REPLACE any previously-set UA (e.g. the Mangarr default added in
                        // GetResponseAsync), not append to it. UserAgent.ParseAdd is additive,
                        // so clear the collection first to guarantee replacement semantics.
                        webRequest.Headers.UserAgent.Clear();
                        webRequest.Headers.UserAgent.ParseAdd(header.Value);
                        break;
                    case "Proxy-Connection":
                        throw new NotImplementedException();
                    default:
                        webRequest.Headers.Add(header.Key, header.Value);
                        break;
                }
            }
        }

        private static void AddContentHeader(HttpRequestMessage request, string header, string value)
        {
            var headers = request.Content?.Headers;
            if (headers == null)
            {
                return;
            }

            headers.Remove(header);
            headers.Add(header, value);
        }

        private CredentialCache GetCredentialCache()
        {
            return _credentialCache.Get("credentialCache", () => new CredentialCache());
        }
    }
}
