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
    /// Phase 17 Wave 1 fixture — idle teardown path (D-12): with default
    /// <c>IdleTimeout = TimeSpan.FromMinutes(10)</c>, the warm Browser/Page is torn down
    /// after the configured idle interval; the next <see cref="IComixSigner.ProxyFetchAsync"/>
    /// must cold-spawn (Browser launch + page load + probe) again.
    ///
    /// <para>
    /// Test override pattern: subclasses <c>ComixPuppeteerSigner</c> with a
    /// <c>protected override TimeSpan IdleTimeout =&gt; TimeSpan.FromMilliseconds(50);</c>
    /// override to keep tests fast. D-12 keeps the production const at 10 min; the
    /// fixture override is the test-only knob.
    /// </para>
    /// </summary>
    [TestFixture]
    public class ComixSignerIdleTeardownFixture : CoreTest
    {
        // FastIdleSigner: shrinks IdleTimeout to 50 ms and overrides the page-context
        // seams so the test can drive the lifecycle without a real Chromium child.
        // Uses Moq's IBrowser mock to satisfy the production code's _browser field shape;
        // CloseAsync flips a flag the test asserts on.
        private class FastIdleSigner : ComixPuppeteerSigner
        {
            public int LaunchCount;
            public int CloseCount;

            // CI-infra (ci-test-jobs-latent-faults): IdleTimeout is now per-instance so each
            // test picks a value with a safe margin. The pre-fix hard-coded 50 ms made
            // Activity_within_idle_window_should_NOT_tear_down flaky on loaded CI runners —
            // a `Task.Delay(20)` between calls can balloon past 50 ms under scheduler
            // pressure, firing the idle timer and producing a spurious second launch.
            private readonly TimeSpan _idleTimeout;

            public FastIdleSigner(IIndexerSourceStatusService s, Logger l, TimeSpan idleTimeout)
                : base(s, new Moq.Mock<NzbDrone.Core.Indexers.Cloudflare.ICloudflareClearanceService>().Object, new Moq.Mock<NzbDrone.Core.Configuration.IConfigService>().Object, l)
            {
                _idleTimeout = idleTimeout;
            }

            protected override TimeSpan IdleTimeout => _idleTimeout;

            protected override Task LaunchAndProbeAsync(CancellationToken ct)
            {
                LaunchCount++;
                var mockBrowser = new Mock<IBrowser>();
                mockBrowser
                    .Setup(b => b.CloseAsync())
                    .Returns(() =>
                    {
                        CloseCount++;
                        return Task.CompletedTask;
                    });
                SetBrowserField(mockBrowser.Object);
                return Task.CompletedTask;
            }

            protected override Task<string> EvaluateProxyFetchAsync(string apiPath, CancellationToken ct)
                => Task.FromResult($"{{\"path\":\"{apiPath}\"}}");

            protected override async Task<string> ProxyFetchAsyncImpl(string apiPath, CancellationToken ct)
            {
                var gate = GetGateField();

                await gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    if (GetBrowserField() == null)
                    {
                        await LaunchAndProbeAsync(ct).ConfigureAwait(false);
                    }

                    InvokeReArmIdleTimer();
                    return await EvaluateProxyFetchAsync(apiPath, ct).ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        gate.Release();
                    }
                    catch
                    {
                        // Race-safe: Dispose can have disposed the gate.
                    }
                }
            }

            // ── Private base-field accessors for test wiring ─────────────────────────
            private SemaphoreSlim GetGateField()
            {
                var f = typeof(ComixPuppeteerSigner).GetField("_gate",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                return (SemaphoreSlim)f.GetValue(this);
            }

            private void SetBrowserField(IBrowser browser)
            {
                var f = typeof(ComixPuppeteerSigner).GetField("_browser",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                f.SetValue(this, browser);
            }

            private IBrowser GetBrowserField()
            {
                var f = typeof(ComixPuppeteerSigner).GetField("_browser",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                return (IBrowser)f.GetValue(this);
            }

            private void InvokeReArmIdleTimer()
            {
                var m = typeof(ComixPuppeteerSigner).GetMethod("ReArmIdleTimer",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                m.Invoke(this, null);
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
        public async Task After_idle_period_browser_should_tear_down_and_next_call_cold_spawns()
        {
            // Short IdleTimeout — this test WANTS the timer to fire quickly so teardown is
            // observable; the 300 ms + 100 ms waits below are comfortably past it.
            var subject = new FastIdleSigner(
                Mocker.GetMock<IIndexerSourceStatusService>().Object,
                LogManager.GetLogger("ComixPuppeteerSigner"),
                TimeSpan.FromMilliseconds(50));

            await subject.ProxyFetchAsync("/manga/test/chapters").ConfigureAwait(false);
            subject.LaunchCount.Should().Be(1, "first call must spawn");

            // Wait > IdleTimeout (50ms) so the timer fires.
            await Task.Delay(300).ConfigureAwait(false);

            // The idle timer's OnIdleElapsed handler is async-void; give it scheduler time
            // to acquire the gate + run TeardownBrowserAsync.
            await Task.Delay(100).ConfigureAwait(false);

            subject.CloseCount.Should().BeGreaterOrEqualTo(1, "idle timer should have called CloseAsync");

            await subject.ProxyFetchAsync("/manga/test/chapters").ConfigureAwait(false);
            subject.LaunchCount.Should().Be(2, "second call should cold-spawn after teardown");

            _memoryTarget.Logs.Should().Contain(l =>
                l.Contains("Comix signer: idle") && l.Contains("min, browser torn down."));
        }

        [Test]
        public async Task Activity_within_idle_window_should_NOT_tear_down()
        {
            // Pitfall 2: ReArmIdleTimer runs BEFORE _gate.Release() so back-to-back calls
            // each push the timer forward; only ONE launch occurs across N consecutive
            // ProxyFetchAsync calls within the idle window.
            //
            // Generous 5 s IdleTimeout: the assertion is "rapid re-arming keeps the browser
            // alive", which only needs each inter-call gap to stay UNDER the timeout. The
            // whole 5-iteration loop runs in well under 5 s even on a heavily loaded CI
            // runner, so the timer reliably never fires — eliminating the pre-fix flakiness
            // where a 20 ms delay could balloon past a 50 ms timeout under scheduler pressure.
            var subject = new FastIdleSigner(
                Mocker.GetMock<IIndexerSourceStatusService>().Object,
                LogManager.GetLogger("ComixPuppeteerSigner"),
                TimeSpan.FromSeconds(5));

            for (var i = 0; i < 5; i++)
            {
                await subject.ProxyFetchAsync("/manga/test/chapters").ConfigureAwait(false);

                // Small gap to simulate rapid-but-not-instant calls; trivially under the 5 s timeout.
                await Task.Delay(20).ConfigureAwait(false);
            }

            subject.LaunchCount.Should().Be(1,
                "5 rapid back-to-back calls should re-arm the timer; only ONE spawn observed");
        }
    }
}
