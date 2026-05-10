using System;
using System.Threading.Tasks;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.Comix
{
    /// <summary>
    /// Phase 17 Wave 0 RED fixture — fail-soft path (D-10 + D-11): when Browser.LaunchAsync
    /// (or probe) throws, the signer must call
    /// <see cref="IIndexerSourceStatusService.RecordFailure(string, TimeSpan)"/> with
    /// <c>"comix.to"</c> so the existing 4-level escalation surfaces an
    /// <c>IndexerSourceFailureCheck</c> Health Check warning at the 3-consecutive threshold
    /// (RESEARCH §"IIndexerSourceStatusService"; ComixIndexer.cs:66 DefaultSourceKey).
    ///
    /// <para>
    /// Wave 1 (Plan 17-02 Task 1b) un-Ignores this fixture AFTER authoring
    /// <c>ComixPuppeteerSigner</c> with a <c>protected virtual Task&lt;IBrowser&gt;
    /// LaunchBrowserAsync(CancellationToken)</c> seam (or equivalent factory delegate)
    /// that the test subclass can override to throw. Wave 0 leaves a TODO for the
    /// subclass shape since <c>ComixPuppeteerSigner</c> doesn't exist yet.
    /// </para>
    /// </summary>
    [TestFixture]
    [Ignore("WAVE-1-DEP: requires ComixPuppeteerSigner concrete impl + LaunchBrowserAsync seam from Plan 17-02 Task 1b")]
    public class ComixSignerFailSoftFixture : CoreTest
    {
        // Wave 1 subclass shape (documented here so the un-ignore path is mechanical):
        //
        //   private class ThrowingSigner : ComixPuppeteerSigner
        //   {
        //       public ThrowingSigner(IIndexerSourceStatusService s, NLog.Logger l) : base(s, l) { }
        //       protected override Task<PuppeteerSharp.IBrowser> LaunchBrowserAsync(CancellationToken ct)
        //           => throw new System.Net.Http.HttpRequestException("simulated launch failure");
        //   }
        //
        // The test then uses Mocker.Resolve<ThrowingSigner>() and asserts
        // RecordFailure("comix.to", It.IsAny<TimeSpan>()) was called at least once.

        [Test]
        public async Task ProxyFetchAsync_should_RecordFailure_when_browser_launch_throws()
        {
            // Wave 1 fills the body. Wave 0 stub references the contract:
            // - subclass must throw on LaunchBrowserAsync
            // - assert: Mocker.GetMock<IIndexerSourceStatusService>()
            //               .Verify(s => s.RecordFailure("comix.to", It.IsAny<TimeSpan>()),
            //                       Times.AtLeastOnce());
            // - assert: original exception is rethrown so caller (Fetch) can react.
            await Task.Yield();
            Assert.Fail("WAVE-1-DEP: implement after Plan 17-02 Task 1b lands ComixPuppeteerSigner.");
        }

        [Test]
        public async Task After_RecordFailure_subsequent_call_should_retry_from_cold()
        {
            // D-10: "Next request retries from cold." Verify by:
            //  1. First ProxyFetchAsync throws + RecordFailure called (n=1).
            //  2. Reconfigure subclass to succeed on second LaunchBrowserAsync.
            //  3. Second ProxyFetchAsync succeeds, returns canned JSON.
            //  4. Assert LaunchBrowserAsync was attempted exactly twice.
            await Task.Yield();
            Assert.Fail("WAVE-1-DEP: implement after Plan 17-02 Task 1b.");
        }

        [Test]
        public async Task Mangarr_stays_up_after_signer_failure()
        {
            // Sanity: signer failure must NOT bubble to a process-level exception.
            // ComixIndexer.Fetch catches and returns empty release list.
            // Verify by: install ThrowingSigner; call ComixIndexer.Fetch; assert empty list returned, no throw.
            await Task.Yield();
            Assert.Fail("WAVE-1-DEP: implement after Plan 17-02 Task 1b.");
        }

        // Reference assertions (Wave 1 elaboration):
        //   Mocker.GetMock<IIndexerSourceStatusService>()
        //         .Verify(s => s.RecordFailure("comix.to", It.IsAny<TimeSpan>()),
        //                 Times.AtLeastOnce());
        //
        // Pattern source: src/NzbDrone.Core.Test/Download/Manga/MangaDownloadServiceFixture.cs:116-128
        // Phase 17 D-10 callsite: ComixPuppeteerSigner.LaunchAndProbeAsync catch block.
    }
}
