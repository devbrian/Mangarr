using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Comix;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.Comix
{
    /// <summary>
    /// Phase 17 Wave 1 fixture — lazy re-probe-on-EvaluateAsync-error
    /// (revision iteration 1, B-2 path (a); plan-side decision retires D-09's "deferred").
    ///
    /// <para>
    /// When <c>EvaluateProxyFetchAsync</c> throws on the warm page (e.g. comix.to mid-session
    /// re-deploy invalidates the cached <c>(signerExpr, installerExpr)</c>), the impl must:
    /// </para>
    ///
    /// <list type="number">
    ///   <item><description>Tear down the stale page + browser.</description></item>
    ///   <item><description>Call <c>LaunchAndProbeAsync</c> once more (cold spawn).</description></item>
    ///   <item><description>Retry the original <c>ProxyFetchAsync</c> call ONCE.</description></item>
    ///   <item><description>If the retry also throws, fall through to <c>RecordFailure</c> + rethrow (no infinite recurse).</description></item>
    /// </list>
    /// </summary>
    [TestFixture]
    public class ComixSignerLazyReprobeFixture : CoreTest
    {
        // ReprobableSigner: counts launches + evaluates; ShouldThrow predicate decides
        // whether each EvaluateProxyFetchAsync attempt throws. Lets us drive the lazy
        // reprobe's path-(a) "first throw → retry succeeds" + path-(b) "both throw →
        // RecordFailure + rethrow" without a real Chromium child.
        private class ReprobableSigner : ComixPlaywrightSigner
        {
            public int LaunchCount;
            public int EvalCount;
            public Func<int, bool> ShouldThrow { get; set; } = _ => false;

            public ReprobableSigner(IIndexerSourceStatusService s, Logger l)
                : base(s, new Moq.Mock<NzbDrone.Core.Indexers.Cloudflare.ICloudflareClearanceService>().Object, new Moq.Mock<NzbDrone.Core.Configuration.IConfigService>().Object, l)
            {
            }

            protected override Task<IBrowser> LaunchBrowserAsync(CancellationToken ct)
            {
                LaunchCount++;
                var mockBrowser = new Mock<IBrowser>();
                mockBrowser.Setup(b => b.CloseAsync()).Returns(Task.CompletedTask);
                return Task.FromResult(mockBrowser.Object);
            }

            protected override async Task LaunchAndProbeAsync(CancellationToken ct)
            {
                var browser = await LaunchBrowserAsync(ct).ConfigureAwait(false);
                SetBrowserField(browser);
            }

            protected override Task<string> EvaluateProxyFetchAsync(string apiPath, CancellationToken ct)
            {
                var n = ++EvalCount;
                if (ShouldThrow(n))
                {
                    throw new InvalidOperationException($"simulated stale-page on call {n}");
                }

                return Task.FromResult($"{{\"call\":{n}}}");
            }

            private void SetBrowserField(IBrowser browser)
            {
                var f = typeof(ComixPlaywrightSigner).GetField("_browser",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                f.SetValue(this, browser);
            }
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
        public async Task EvaluateAsync_throw_on_first_call_should_relaunch_and_retry_once()
        {
            var subject = new ReprobableSigner(
                Mocker.GetMock<IIndexerSourceStatusService>().Object,
                LogManager.GetLogger("ComixPlaywrightSigner"))
            {
                ShouldThrow = n => n == 1,
            };

            var result = await subject.ProxyFetchAsync("/manga/test/chapters");

            result.Should().Contain("\"call\":2", "second call returned successfully after relaunch");
            subject.LaunchCount.Should().Be(2, "initial + ONE relaunch");
            subject.EvalCount.Should().Be(2, "first throw + second success");

            _memoryTarget.Logs.Should().Contain(l =>
                l.StartsWith("Warn|")
                && l.Contains("Comix signer: stale page detected (EvaluateAsync threw); relaunching"));
        }

        [Test]
        public async Task EvaluateAsync_throw_on_BOTH_calls_should_RecordFailure_and_rethrow()
        {
            var subject = new ReprobableSigner(
                Mocker.GetMock<IIndexerSourceStatusService>().Object,
                LogManager.GetLogger("ComixPlaywrightSigner"))
            {
                ShouldThrow = _ => true,   // every call throws
            };

            Func<Task> act = () => subject.ProxyFetchAsync("/manga/test/chapters");
            await act.Should().ThrowAsync<InvalidOperationException>();

            subject.LaunchCount.Should().Be(2,
                "exactly initial + ONE relaunch — no infinite recurse");
            subject.EvalCount.Should().Be(2);

            Mocker.GetMock<IIndexerSourceStatusService>()
                  .Verify(s => s.RecordFailure("comix.to", It.IsAny<TimeSpan>()), Times.AtLeastOnce());
        }

        [Test]
        public async Task Reprobe_warning_must_NOT_log_apiPath_value()
        {
            // T-17-01-02 mitigation guard: the reprobe Warn line is allowed to log a
            // fixed substring "stale page detected" + "relaunching" — but MUST NOT log
            // the apiPath value (potentially attacker-controlled per T-17-02-01) nor any
            // EvaluateAsync exception bytes that might contain page-context state.
            const string ApiPath = "/manga/sensitive_token_value/chapters";
            var subject = new ReprobableSigner(
                Mocker.GetMock<IIndexerSourceStatusService>().Object,
                LogManager.GetLogger("ComixPlaywrightSigner"))
            {
                ShouldThrow = n => n == 1,
            };

            await subject.ProxyFetchAsync(ApiPath);

            // Find the lazy-reprobe warn line and assert it does NOT contain the apiPath.
            var reprobeWarn = string.Empty;
            foreach (var line in _memoryTarget.Logs)
            {
                if (line.StartsWith("Warn|") && line.Contains("stale page detected"))
                {
                    reprobeWarn = line;
                    break;
                }
            }

            reprobeWarn.Should().NotBeNullOrEmpty();
            reprobeWarn.Should().NotContain("sensitive_token_value",
                "lazy-reprobe Warn must NOT leak apiPath bytes (V7 / T-17-01-02)");
        }
    }
}
