using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Comix;
using NzbDrone.Core.Test.Framework;
using PuppeteerSharp;

namespace NzbDrone.Core.Test.Indexers.Comix
{
    /// <summary>
    /// Phase 17 Wave 1 fixture — fail-soft path (D-10 + D-11): when Browser.LaunchAsync
    /// (or probe) throws, the signer must call
    /// <see cref="IIndexerSourceStatusService.RecordFailure(string, TimeSpan)"/> with
    /// <c>"comix.to"</c> so the existing 4-level escalation surfaces an
    /// <c>IndexerSourceFailureCheck</c> Health Check warning at the 3-consecutive threshold.
    /// </summary>
    [TestFixture]
    public class ComixSignerFailSoftFixture : CoreTest
    {
        // ThrowingSigner: every LaunchBrowserAsync attempt throws. Used to exercise
        // the fail-soft + RecordFailure path without a real Chromium child.
        private class ThrowingSigner : ComixPuppeteerSigner
        {
            public int LaunchAttempts;

            public ThrowingSigner(IIndexerSourceStatusService s, Logger l)
                : base(s, l)
            {
            }

            protected override Task<IBrowser> LaunchBrowserAsync(CancellationToken ct)
            {
                LaunchAttempts++;
                throw new HttpRequestException("simulated launch failure");
            }
        }

        // RecoveringSigner: throws on the first launch attempt, succeeds on the second
        // (after RecordFailure is called and the next request retries from cold).
        // Uses a Mock<IBrowser> with no real Chromium.
        private class RecoveringSigner : ComixPuppeteerSigner
        {
            public int LaunchAttempts;

            public RecoveringSigner(IIndexerSourceStatusService s, Logger l)
                : base(s, l)
            {
            }

            protected override Task<IBrowser> LaunchBrowserAsync(CancellationToken ct)
            {
                LaunchAttempts++;
                if (LaunchAttempts == 1)
                {
                    throw new HttpRequestException("simulated launch failure");
                }

                return Task.FromResult(new Mock<IBrowser>().Object);
            }

            // We can't run the real probe without a real page; bypass LaunchAndProbeAsync.
            protected override async Task LaunchAndProbeAsync(CancellationToken ct)
            {
                await LaunchBrowserAsync(ct).ConfigureAwait(false);
                SetBrowserField(new Mock<IBrowser>().Object);
            }

            protected override Task<string> EvaluateProxyFetchAsync(string apiPath, CancellationToken ct)
                => Task.FromResult("{\"recovered\":true}");

            private void SetBrowserField(IBrowser browser)
            {
                var f = typeof(ComixPuppeteerSigner).GetField("_browser",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                f.SetValue(this, browser);
            }
        }

        [Test]
        public async Task ProxyFetchAsync_should_RecordFailure_when_browser_launch_throws()
        {
            var subject = new ThrowingSigner(
                Mocker.GetMock<IIndexerSourceStatusService>().Object,
                LogManager.GetLogger("ComixPuppeteerSigner"));

            Func<Task> act = () => subject.ProxyFetchAsync("/manga/test/chapters");
            await act.Should().ThrowAsync<HttpRequestException>();

            Mocker.GetMock<IIndexerSourceStatusService>()
                  .Verify(s => s.RecordFailure("comix.to", It.IsAny<TimeSpan>()), Times.AtLeastOnce());
        }

        [Test]
        public async Task After_RecordFailure_subsequent_call_should_retry_from_cold()
        {
            var subject = new RecoveringSigner(
                Mocker.GetMock<IIndexerSourceStatusService>().Object,
                LogManager.GetLogger("ComixPuppeteerSigner"));

            // First call throws (LaunchAttempts == 1, throws inside LaunchAndProbeAsync).
            Func<Task> act1 = () => subject.ProxyFetchAsync("/manga/test/chapters");
            await act1.Should().ThrowAsync<HttpRequestException>();

            // Second call: LaunchAttempts == 2, succeeds; lazy-reprobe path is NOT
            // exercised here because the first call's failure tore down the browser
            // before the EvaluateAsync seam was reached — the next call cold-spawns
            // through LaunchAndProbeAsync's success path.
            //
            // (The launch counter we observe will be 2 — initial throw + cold respawn.
            // The lazy-reprobe fixture covers the EvaluateAsync-side retry separately.)
            var result = await subject.ProxyFetchAsync("/manga/test/chapters");

            result.Should().Be("{\"recovered\":true}");
            subject.LaunchAttempts.Should().Be(2,
                "first call's failure tears down; second call cold-spawns through a successful relaunch");
        }

        [Test]
        public async Task Signer_failure_surfaces_via_RecordFailure_not_process_termination()
        {
            // T-17-02-04 mitigation: signer failure must NOT bubble to a process-level
            // exception. ComixIndexer.Fetch (Task 2b) catches at the indexer layer; this
            // fixture proves the signer surface honors the contract — no AppDomain
            // unhandled exception, no FailFast — exception flows to caller via Task throw.
            var subject = new ThrowingSigner(
                Mocker.GetMock<IIndexerSourceStatusService>().Object,
                LogManager.GetLogger("ComixPuppeteerSigner"));

            Func<Task> act = () => subject.ProxyFetchAsync("/manga/test/chapters");
            await act.Should().ThrowAsync<HttpRequestException>(
                "exception must surface as a Task throw, not a process-level crash");
        }
    }
}
