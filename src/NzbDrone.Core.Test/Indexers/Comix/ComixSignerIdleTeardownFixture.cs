using System;
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

            public FastIdleSigner(IIndexerSourceStatusService s, Logger l)
                : base(s, l)
            {
            }

            protected override TimeSpan IdleTimeout => TimeSpan.FromMilliseconds(50);

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
            var subject = new FastIdleSigner(
                Mocker.GetMock<IIndexerSourceStatusService>().Object,
                LogManager.GetLogger("ComixPuppeteerSigner"));

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
            // ProxyFetchAsync calls within the FastIdle window.
            var subject = new FastIdleSigner(
                Mocker.GetMock<IIndexerSourceStatusService>().Object,
                LogManager.GetLogger("ComixPuppeteerSigner"));

            for (var i = 0; i < 5; i++)
            {
                await subject.ProxyFetchAsync("/manga/test/chapters").ConfigureAwait(false);

                // Sleep 20ms — well under IdleTimeout (50ms) so the timer should NOT fire.
                await Task.Delay(20).ConfigureAwait(false);
            }

            subject.LaunchCount.Should().Be(1,
                "5 rapid back-to-back calls should re-arm the timer; only ONE spawn observed");
        }
    }
}
