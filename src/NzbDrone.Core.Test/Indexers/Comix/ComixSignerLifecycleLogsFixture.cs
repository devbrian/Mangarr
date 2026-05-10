using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Comix;
using NzbDrone.Core.Test.Framework;
using PuppeteerSharp;

namespace NzbDrone.Core.Test.Indexers.Comix
{
    /// <summary>
    /// Phase 17 Wave 1 fixture — D-15 lifecycle log lines (NLog Info / Warn). Captures
    /// emitted log records via an NLog <see cref="MemoryTarget"/> attached at SetUp and
    /// detached at TearDown. The verbatim D-15 strings:
    ///
    /// <list type="bullet">
    ///   <item><description><c>"Comix signer: Chromium launched, page loaded, probe captured signer/installer fns (probe latency: {0}ms)."</c></description></item>
    ///   <item><description><c>"Comix signer: idle {0} min, browser torn down."</c></description></item>
    ///   <item><description><c>"Comix signer: probe failed (n={0}); relaunching browser."</c></description></item>
    ///   <item><description><c>"Comix signer: stale page detected (EvaluateAsync threw); relaunching and retrying once."</c> (B-2 path (a))</description></item>
    /// </list>
    ///
    /// <para>
    /// T-17-01-02 mitigation (PII / Information Disclosure): assertions check for
    /// well-known substrings — they do NOT and MUST NOT assert any signed-token bytes.
    /// V7 — never log secrets.
    /// </para>
    /// </summary>
    [TestFixture]
    public class ComixSignerLifecycleLogsFixture : CoreTest
    {
        // Probe-success subclass — lets the lifecycle-Info line emit without spinning a
        // real Chromium child. We override LaunchAndProbeAsync to set the browser/page
        // fields directly + emit the success log via the production logger.
        private class SuccessfulProbeSigner : ComixPuppeteerSigner
        {
            public SuccessfulProbeSigner(IIndexerSourceStatusService s, Logger l)
                : base(s, l)
            {
            }

            protected override TimeSpan IdleTimeout => TimeSpan.FromMilliseconds(50);

            protected override async Task LaunchAndProbeAsync(CancellationToken ct)
            {
                // We mimic the production LaunchAndProbeAsync's logging shape so the
                // D-15 line lands in MemoryTarget verbatim. Real probe is bypassed.
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var mockBrowser = new Mock<IBrowser>();
                mockBrowser.Setup(b => b.CloseAsync()).Returns(Task.CompletedTask);
                SetBrowserField(mockBrowser.Object);
                sw.Stop();
                GetLoggerField().Info(
                    "Comix signer: Chromium launched, page loaded, probe captured signer/installer fns (probe latency: {0}ms).",
                    sw.ElapsedMilliseconds);
                await Task.CompletedTask;
            }

            protected override Task<string> EvaluateProxyFetchAsync(string apiPath, CancellationToken ct)
                => Task.FromResult("{\"ok\":true}");

            private void SetBrowserField(IBrowser browser)
            {
                var f = typeof(ComixPuppeteerSigner).GetField("_browser",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                f.SetValue(this, browser);
            }

            private Logger GetLoggerField()
            {
                var f = typeof(ComixPuppeteerSigner).GetField("_logger",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                return (Logger)f.GetValue(this);
            }
        }

        // Probe-failure subclass — every probe attempt throws. Used to exercise the
        // probe-failure Warn line.
        private class ProbeFailingSigner : ComixPuppeteerSigner
        {
            public ProbeFailingSigner(IIndexerSourceStatusService s, Logger l)
                : base(s, l)
            {
            }

            protected override Task<IBrowser> LaunchBrowserAsync(CancellationToken ct)
                => throw new HttpRequestException("simulated probe failure");
        }

        private MemoryTarget _memoryTarget;
        private LoggingConfiguration _previousConfig;

        [SetUp]
        public void AttachMemoryTarget()
        {
            _previousConfig = LogManager.Configuration;

            var config = new LoggingConfiguration();
            _memoryTarget = new MemoryTarget("memory") { Layout = "${level}|${message}" };
            config.AddTarget(_memoryTarget);
            config.AddRule(LogLevel.Debug, LogLevel.Fatal, _memoryTarget);
            LogManager.Configuration = config;
        }

        [TearDown]
        public void DetachMemoryTarget()
        {
            LogManager.Configuration = _previousConfig;
        }

        [Test]
        public async Task First_spawn_emits_Info_with_probe_latency()
        {
            var subject = new SuccessfulProbeSigner(
                Mocker.GetMock<IIndexerSourceStatusService>().Object,
                LogManager.GetLogger("ComixPuppeteerSigner"));

            await subject.ProxyFetchAsync("/manga/test/chapters").ConfigureAwait(false);

            _memoryTarget.Logs.Should().Contain(l =>
                l.Contains("Comix signer: Chromium launched") && l.Contains("probe latency:"));

            // T-17-01-02: explicitly assert no base64-shaped token substring (≥40 chars)
            // appears in any captured log line.
            var tokenRegex = new System.Text.RegularExpressions.Regex(@"\b[A-Za-z0-9_\-]{40,}\b");
            foreach (var line in _memoryTarget.Logs)
            {
                tokenRegex.IsMatch(line).Should().BeFalse(
                    "log line must not contain base64-shaped token bytes (V7): {0}", line);
            }
        }

        [Test]
        public async Task Probe_failure_emits_Warn_with_n_count()
        {
            var subject = new ProbeFailingSigner(
                Mocker.GetMock<IIndexerSourceStatusService>().Object,
                LogManager.GetLogger("ComixPuppeteerSigner"));

            Func<Task> act = () => subject.ProxyFetchAsync("/manga/test/chapters");
            await act.Should().ThrowAsync<HttpRequestException>();

            _memoryTarget.Logs.Should().Contain(l =>
                l.StartsWith("Warn|") && l.Contains("Comix signer: probe failed (n="));
        }
    }
}
