using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Comix;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.Comix
{
    /// <summary>
    /// Phase 17 Wave 1 fixture — shutdown path (D-05): when
    /// <see cref="ApplicationShutdownRequested"/> fires, the signer must dispose the warm
    /// Browser, set the disposed flag, and reject all subsequent
    /// <see cref="IComixSigner.ProxyFetchAsync"/> calls with
    /// <see cref="ObjectDisposedException"/>.
    ///
    /// <para>
    /// W-2 absorption (revision iteration 1): ALSO covers the in-flight-during-shutdown
    /// drain race. If a ProxyFetchAsync is mid-flight (holding _gate via SemaphoreSlim)
    /// when Handle(ApplicationShutdownRequested) fires, the dispose path completes within
    /// the 6-second drain window AND emits the verbatim shutdown-timeout warning on
    /// overrun.
    /// </para>
    /// </summary>
    [TestFixture]
    public class ComixSignerShutdownFixture : CoreTest
    {
        // GatedSigner: first ProxyFetchAsync acquires _gate (production base) and then
        // blocks on a TaskCompletionSource (overridden seam) so the test can reproduce the
        // "in-flight when Handle fires" race.
        private class GatedSigner : ComixPlaywrightSigner
        {
            public TaskCompletionSource<string> Gate { get; } = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            public GatedSigner(IIndexerSourceStatusService s, Logger l)
                : base(s, new Moq.Mock<NzbDrone.Core.Indexers.Cloudflare.ICloudflareClearanceService>().Object, new Moq.Mock<NzbDrone.Core.Configuration.IConfigService>().Object, l)
            {
            }

            // Override ProxyFetchAsyncImpl directly so we don't run the base's
            // gate/launch/evaluate flow (which throws NotImplementedException in Task 1a).
            // Instead, acquire the gate ourselves to faithfully mimic production locking,
            // then block on the TCS.
            protected override async Task<string> ProxyFetchAsyncImpl(string apiPath, CancellationToken ct)
            {
                // Use reflection to invoke the private _gate field on the base class —
                // the test must hold the gate the same way production does so Dispose's
                // drain semantics actually wait.
                var gateField = typeof(ComixPlaywrightSigner).GetField("_gate",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var gate = (SemaphoreSlim)gateField.GetValue(this);

                await gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    return await Gate.Task.ConfigureAwait(false);
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
        public async Task ApplicationShutdownRequested_should_dispose_and_subsequent_calls_throw()
        {
            // WR-01 mitigation (revision iteration 2): the previous version of this test
            // was non-async and called `act.Should().ThrowAsync<ObjectDisposedException>();`
            // without awaiting. ThrowAsync returns a `Task<ExceptionAssertions<...>>` —
            // an unawaited Task is silently dropped by NUnit, so the assertion never
            // executed. The test passed regardless of what the SUT did. Make the method
            // async + await the assertion so the contract is actually enforced.
            var subject = new ComixPlaywrightSigner(
                Mocker.GetMock<IIndexerSourceStatusService>().Object,
                Mocker.GetMock<NzbDrone.Core.Indexers.Cloudflare.ICloudflareClearanceService>().Object,
                Mocker.GetMock<NzbDrone.Core.Configuration.IConfigService>().Object,
                LogManager.GetLogger("ComixPlaywrightSigner"));

            subject.Handle(new ApplicationShutdownRequested(restarting: false));

            Func<Task> act = () => subject.ProxyFetchAsync("/manga/test/chapters");
            await act.Should().ThrowAsync<ObjectDisposedException>();
        }

        [Test]
        public void Handle_should_log_Info_with_ApplicationShutdownRequested_received()
        {
            var subject = new ComixPlaywrightSigner(
                Mocker.GetMock<IIndexerSourceStatusService>().Object,
                Mocker.GetMock<NzbDrone.Core.Indexers.Cloudflare.ICloudflareClearanceService>().Object,
                Mocker.GetMock<NzbDrone.Core.Configuration.IConfigService>().Object,
                LogManager.GetLogger("ComixPlaywrightSigner"));

            subject.Handle(new ApplicationShutdownRequested());

            _memoryTarget.Logs.Should().Contain(l => l.Contains("Comix signer: ApplicationShutdownRequested received; disposing browser."));
        }

        [Test]
        public async Task Dispose_with_in_flight_request_should_log_drain_timeout_warning_and_complete_within_6s()
        {
            // (W-2 absorption — revision iteration 1)
            // 1. Construct GatedSigner whose ProxyFetchAsync blocks on a TaskCompletionSource.
            // 2. Start ProxyFetchAsync on a background task; it will acquire _gate via
            //    SemaphoreSlim.WaitAsync() then block on the TCS.
            // 3. Stopwatch the dispose path; assert it completes in < 6s and emits the
            //    verbatim drain-timeout warning.
            var subject = new GatedSigner(
                Mocker.GetMock<IIndexerSourceStatusService>().Object,
                LogManager.GetLogger("ComixPlaywrightSigner"));

            // Background task acquires the gate inside ProxyFetchAsyncImpl, then blocks.
            var bgTask = Task.Run(() => subject.ProxyFetchAsync("/manga/test/chapters"));

            // Give the background task time to enter the gate.
            // The ProxyFetchAsync entry path checks _disposed (sync) BEFORE acquiring,
            // so we yield enough scheduler ticks for it to land inside ProxyFetchAsyncImpl.
            await Task.Delay(100).ConfigureAwait(false);

            var sw = Stopwatch.StartNew();
            subject.Handle(new ApplicationShutdownRequested());
            sw.Stop();

            sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(6),
                "shutdown drain window must close within 6s even with stuck in-flight call");

            _memoryTarget.Logs.Should().Contain(l =>
                l.Contains("Comix signer: shutdown timed out waiting for in-flight request — forcing teardown"));

            // Release the gated TCS to let the background task unwind cleanly.
            subject.Gate.TrySetResult("released");
            try
            {
                await bgTask.ConfigureAwait(false);
            }
            catch
            {
                // The background task may complete cleanly OR throw (gate disposed under it).
                // Either outcome satisfies the contract — Dispose moved on.
            }
        }
    }
}
