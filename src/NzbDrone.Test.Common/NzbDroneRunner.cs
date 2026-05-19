using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Xml.Linq;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Processes;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using RestSharp;

namespace NzbDrone.Test.Common
{
    public class NzbDroneRunner
    {
        // GH #113: the readiness loop polls an AUTHENTICATED endpoint and requires
        // both transport completion (ResponseStatus.Completed) AND a non-error HTTP
        // status (response.IsSuccessful). RestSharp's ResponseStatus is transport-
        // level only — 401 / 404 / 500 all surface as ResponseStatus.Completed, so
        // checking it in isolation silently green-lights a host that has bound its
        // listener but not yet wired ApiKeyAuthenticationHandler (race window b).
        //
        // The loop further requires TWO consecutive successful probes spaced
        // ~250 ms apart so a single moment of "good" doesn't fool us through a
        // Kestrel listener-flap (race window a) where the probe succeeds and the
        // very next caller hits a torn-down listener (StatusCode == 0).
        //
        // The probe base URL must point at the LIVE API surface — Phase 15 Plan
        // 15-06 deleted Sonarr.Api.V3 entirely, so the historical /api/v3 base
        // returns 404 for every request. Switched to /api/v5 (the current primary
        // API surface, matching what TestKit and the React frontend consume) so
        // the IsSuccessful check actually has a real endpoint to validate against.
        //
        // GH #174 (debug session gh174-urlbase-redirect-spa-bug): when the runner
        // is started with a non-empty urlBase, the API surface moves to
        // `/<urlbase>/api/v5` and the readiness client is rebuilt against that
        // path inside Start(). The default urlBase=string.Empty path is unchanged.
        private const string ApiBasePath = "api/v5";
        private const int ReadinessStableSuccessesRequired = 2;
        private const int ReadinessPollIntervalMs = 250;
        private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(60);

        private readonly IProcessProvider _processProvider;
        private IRestClient _restClient;
        private Process _nzbDroneProcess;

        public string AppData { get; private set; }
        public string ApiKey { get; private set; }
        public PostgresOptions PostgresOptions { get; private set; }
        public int Port { get; private set; }
        public string UrlBase { get; private set; }

        public NzbDroneRunner(Logger logger, PostgresOptions postgresOptions, int port = 0)
        {
            // GH #204: hardcoded port=8989 caused deterministic Windows TIME_WAIT
            // collisions when 200+ Automation fixtures ran back-to-back in a
            // single `dotnet test`. Default port=0 now requests an ephemeral
            // port from the OS via TcpListener — each fixture gets an isolated
            // port and TIME_WAIT on the previous fixture's port no longer
            // gates the next fixture's boot. Callers that need a specific port
            // (e.g. the rare integration test pinned to 8989 for compatibility)
            // can still pass an explicit port.
            if (port == 0)
            {
                port = GetEphemeralLoopbackPort();
            }

            _processProvider = new ProcessProvider(logger);
            _restClient = new RestClient($"http://localhost:{port}/{ApiBasePath}");

            PostgresOptions = postgresOptions;
            Port = port;
            UrlBase = string.Empty;
        }

        // GH #204: ask the OS for an unused loopback TCP port. The TcpListener
        // is created, bound, queried, and immediately stopped — Windows does
        // NOT place the port in TIME_WAIT for a listener that never ACCEPTed
        // an inbound connection, so the port is available for the child
        // Mangarr process to bind immediately after Stop(). A theoretical race
        // exists where another process could grab the port between Stop() and
        // Kestrel's bind, but on a CI test host this is exceedingly rare; if
        // it materialises in practice we can layer SO_REUSEADDR or a retry
        // loop on top.
        private static int GetEphemeralLoopbackPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        public void Start(bool enableAuth = false, string urlBase = "")
        {
            AppData = Path.Combine(TestContext.CurrentContext.TestDirectory, "_intg_" + TestBase.GetUID());
            Directory.CreateDirectory(AppData);

            // Normalise: trim whitespace first so " " collapses to empty,
            // then strip leading/trailing slashes; ConfigFileProvider.UrlBase
            // trims '/' then re-prefixes '/' so we keep the bare segment here.
            // (gh #174 CodeRabbit review — whitespace-only inputs must
            // normalise to empty so GenerateConfigFile omits <UrlBase>.)
            UrlBase = (urlBase ?? string.Empty).Trim().Trim('/');

            // Rebuild the readiness client against the urlBase-prefixed API path
            // when set; default empty path mirrors the legacy constructor URL.
            var apiBaseUrl = UrlBase.IsNullOrWhiteSpace()
                ? $"http://localhost:{Port}/{ApiBasePath}"
                : $"http://localhost:{Port}/{UrlBase}/{ApiBasePath}";
            _restClient = new RestClient(apiBaseUrl);

            GenerateConfigFile(enableAuth, UrlBase);

            string consoleExe;
            if (OsInfo.IsWindows)
            {
                consoleExe = "Mangarr.Console.exe";
            }
            else
            {
                consoleExe = "Mangarr";
            }

            if (BuildInfo.IsDebug)
            {
                Start(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "_output", "net10.0", consoleExe));
            }
            else
            {
                Start(Path.Combine(TestContext.CurrentContext.TestDirectory, "bin", consoleExe));
            }

            WaitForReady();
        }

        private void WaitForReady()
        {
            var deadline = DateTime.UtcNow + ReadinessTimeout;
            var consecutiveSuccesses = 0;

            while (true)
            {
                _nzbDroneProcess.Refresh();

                if (_nzbDroneProcess.HasExited)
                {
                    Assert.Fail("Process has exited");
                }

                if (DateTime.UtcNow >= deadline)
                {
                    Assert.Fail(
                        $"Mangarr {Port} did not become ready within {ReadinessTimeout.TotalSeconds:F0}s " +
                        $"(needed {ReadinessStableSuccessesRequired} consecutive authenticated 200s; " +
                        $"got {consecutiveSuccesses}).");
                }

                var statusCall = ProbeAuthenticatedStatus();

                if (statusCall.IsSuccessful)
                {
                    consecutiveSuccesses++;

                    if (consecutiveSuccesses >= ReadinessStableSuccessesRequired)
                    {
                        TestContext.Progress.WriteLine(
                            $"Mangarr {Port} is started (authenticated probe stable). Running Tests");
                        return;
                    }
                }
                else
                {
                    if (consecutiveSuccesses > 0)
                    {
                        TestContext.Progress.WriteLine(
                            "Mangarr {0} readiness window broken at success #{1}; restarting stability count.",
                            Port,
                            consecutiveSuccesses);
                    }

                    consecutiveSuccesses = 0;

                    TestContext.Progress.WriteLine(
                        "Waiting for Mangarr to start. Response Status : {0}  HTTP {1} [{2}] {3}",
                        statusCall.ResponseStatus,
                        (int)statusCall.StatusCode,
                        statusCall.StatusDescription,
                        statusCall.ErrorException?.Message ?? "<no transport exception>");
                }

                Thread.Sleep(ReadinessPollIntervalMs);
            }
        }

        private IRestResponse ProbeAuthenticatedStatus()
        {
            // Authenticated probe — system/status is the lightest endpoint that
            // exercises the auth handler. Passing both Authorization and X-Api-Key
            // matches the historical request shape so legacy auth wiring is
            // exercised identically.
            var request = new RestRequest("system/status");
            request.AddHeader("Authorization", ApiKey);
            request.AddHeader("X-Api-Key", ApiKey);

            return _restClient.Get(request);
        }

        public void Kill()
        {
            try
            {
                if (_nzbDroneProcess != null)
                {
                    _nzbDroneProcess.Refresh();
                    if (_nzbDroneProcess.HasExited)
                    {
                        var log = File.ReadAllLines(Path.Combine(AppData, "logs", "Mangarr.trace.txt"));
                        var output = log.Join(Environment.NewLine);
                        TestContext.Progress.WriteLine("Process has exited prematurely: ExitCode={0} Output:\n{1}", _nzbDroneProcess.ExitCode, output);
                    }

                    _processProvider.Kill(_nzbDroneProcess.Id);
                }
            }
            catch (InvalidOperationException)
            {
                // May happen if the process closes while being closed
            }

            TestBase.DeleteTempFolder(AppData);
        }

        public void KillAll()
        {
            try
            {
                if (_nzbDroneProcess != null)
                {
                    _processProvider.Kill(_nzbDroneProcess.Id);
                }

                _processProvider.KillAll(ProcessProvider.SONARR_CONSOLE_PROCESS_NAME);
                _processProvider.KillAll(ProcessProvider.SONARR_PROCESS_NAME);
            }
            catch (InvalidOperationException)
            {
                // May happen if the process closes while being closed
            }

            TestBase.DeleteTempFolder(AppData);
        }

        private void Start(string outputSonarrConsoleExe)
        {
            StringDictionary envVars = new();
            if (PostgresOptions?.Host != null)
            {
                envVars.Add("Mangarr__Postgres__Host", PostgresOptions.Host);
                envVars.Add("Mangarr__Postgres__Port", PostgresOptions.Port.ToString());
                envVars.Add("Mangarr__Postgres__User", PostgresOptions.User);
                envVars.Add("Mangarr__Postgres__Password", PostgresOptions.Password);
                envVars.Add("Mangarr__Postgres__MainDb", PostgresOptions.MainDb);
                envVars.Add("Mangarr__Postgres__LogDb", PostgresOptions.LogDb);

                TestContext.Progress.WriteLine("Using env vars:\n{0}", envVars.ToJson());
            }

            TestContext.Progress.WriteLine("Starting instance from {0} on port {1}", outputSonarrConsoleExe, Port);

            var args = "-nobrowser -nosingleinstancecheck -data=\"" + AppData + "\"";
            _nzbDroneProcess = _processProvider.Start(outputSonarrConsoleExe, args, envVars, OnOutputDataReceived, OnOutputDataReceived);
        }

        private void OnOutputDataReceived(string data)
        {
            TestContext.Progress.WriteLine($" [{Port}] > " + data);

            if (data.Contains("Press enter to exit"))
            {
                _nzbDroneProcess.StandardInput.WriteLine(" ");
            }
        }

        private void GenerateConfigFile(bool enableAuth, string urlBase = "")
        {
            var configFile = Path.Combine(AppData, "config.xml");

            // Generate and set the api key so we don't have to poll the config file
            var apiKey = Guid.NewGuid().ToString().Replace("-", "");

            var configElements = new System.Collections.Generic.List<XElement>
            {
                new XElement(nameof(ConfigFileProvider.ApiKey), apiKey),
                new XElement(nameof(ConfigFileProvider.LogLevel), "trace"),
                new XElement(nameof(ConfigFileProvider.AnalyticsEnabled), false),
                new XElement(nameof(ConfigFileProvider.AuthenticationMethod), enableAuth ? "Forms" : "None"),
                new XElement(nameof(ConfigFileProvider.AuthenticationRequired), "DisabledForLocalAddresses"),
                new XElement(nameof(ConfigFileProvider.Port), Port),
            };

            // GH #174 (gh174-urlbase-redirect-spa-bug): emit UrlBase only when
            // requested. ConfigFileProvider.UrlBase normalises by trimming '/'
            // and re-prefixing '/', so persisting the bare segment (e.g.
            // "mangarr") matches its expected shape.
            if (!string.IsNullOrWhiteSpace(urlBase))
            {
                configElements.Add(new XElement(nameof(ConfigFileProvider.UrlBase), urlBase));
            }

            var xDoc = new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                new XElement(ConfigFileProvider.CONFIG_ELEMENT_NAME, configElements));

            var data = xDoc.ToString();

            File.WriteAllText(configFile, data);

            ApiKey = apiKey;
        }
    }
}
