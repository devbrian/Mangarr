using System;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Comix;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.Comix
{
    /// <summary>
    /// Phase 17.2 D-3 / WR-GC-01 closure: the in-IIFE BRANCH-C catch in
    /// <see cref="ComixPuppeteerSigner"/>.<c>EvaluateProxyFetchAsync</c> returns a
    /// <c>{result:null, e:..., decryptError:...}</c> envelope on decrypt-throw. The C# caller
    /// (<see cref="ComixIndexer"/>.<c>DispatchSignerPathsAsync</c> /
    /// <see cref="ComixIndexer"/>.<c>GetChapterPages</c>) MUST detect the envelope and route
    /// the <c>decryptError</c> STRING (NOT the full envelope, NOT the encrypted <c>e</c> blob)
    /// to <see cref="IIndexerSourceStatusService"/>.<c>RecordFailure("comix.to")</c> so the
    /// existing 4-step escalation engages.
    ///
    /// <para>
    /// Chromium-free per D-16 inherited (Mock&lt;IComixSigner&gt; at the unit-test boundary).
    /// </para>
    ///
    /// <para>
    /// T-17.2-11 (Information Disclosure): the encrypted <c>e</c> blob (a base64-encoded
    /// cipher of the full upstream API response — could in principle reveal request shape on
    /// cryptanalysis) MUST NOT appear in any logged Warn line. Verified via NLog
    /// <see cref="MemoryTarget"/> capture mirroring
    /// <c>ComixSignerLifecycleLogsFixture</c>'s pattern (P-L-11).
    /// </para>
    /// </summary>
    [TestFixture]
    public class ComixDecryptErrorEnvelopeRoutingFixture : CoreTest<ComixIndexer>
    {
        // Phase 17.2 D-3 / WR-GC-01 — sample envelope shape produced by the in-IIFE BRANCH-C
        // catch (ComixPuppeteerSigner.cs lines ~418-419):
        //   `}} catch (decryptErr) {{ return JSON.stringify({{ result: null, e: raw.e, decryptError: String(decryptErr) }}); }}`
        // The `e` value below is a 49-char base64url-shaped substring used as the encrypted-blob
        // sentinel; the blob-absence assertion greps for the leading 12-char prefix.
        private const string EncryptedBlobSentinel =
            "_Gt3D-AEerYY4U7WnH2_mL55t-LuXpopdXYaXoQY2B_JHWW-16eLMpONx4rTBHe5T";

        private const string EncryptedBlobSentinelPrefix = "_Gt3D-AEerYY";

        private const string DecryptErrorEnvelope =
            "{\"result\":null,\"e\":\"" + EncryptedBlobSentinel + "\",\"decryptError\":\"TypeError: Cannot read property 'data' of undefined\"}";

        private MemoryTarget _memoryTarget;
        private LoggingConfiguration _previousConfig;

        [SetUp]
        public void Setup()
        {
            // Mirror ComixSignerLifecycleLogsFixture's MemoryTarget capture pattern (P-L-11):
            // swap the global LogManager.Configuration so the production logger ("ComixIndexer")
            // routes through MemoryTarget for the duration of the test, then restore on TearDown.
            _previousConfig = LogManager.Configuration;
            var config = new LoggingConfiguration();
            _memoryTarget = new MemoryTarget("memory") { Layout = "${level}|${message}" };
            config.AddTarget(_memoryTarget);
            config.AddRule(LogLevel.Debug, LogLevel.Fatal, _memoryTarget);
            LogManager.Configuration = config;

            Subject.Definition = new IndexerDefinition
            {
                Id = 2,
                Name = "Comix",
                Settings = new ComixIndexerSettings
                {
                    BaseUrl = "https://comix.to",
                    SourceKey = "comix.to"
                }
            };

            // Mock<IComixSigner> returns the decryptError envelope verbatim — the production
            // code under test must detect the envelope shape BEFORE delegating to the parser,
            // because JsonConvert.DeserializeObject<typed-shape>(envelope) parses to a
            // null-Result POCO that the existing parse path silently swallows (this is the
            // WR-GC-01 finding).
            Mocker.GetMock<IComixSigner>()
                  .Setup(s => s.ProxyFetchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(DecryptErrorEnvelope);

            // ComixIndexer.Fetch(MangaSearchCriteria) calls ResolveMangaHashAsync FIRST
            // (real HTTP via IHttpClient.GetAsync against /api/v1/manga?keyword=...). When
            // the IHttpClient mock returns null/empty content, hid resolves null, Fetch
            // short-circuits with an empty release list, and DispatchSignerPathsAsync is
            // never reached — meaning the signer envelope is never seen by the production
            // code under test. Mock the keyword-search endpoint to return a valid
            // ComixMangaListResponse with one item so resolution yields a non-null hid +
            // dispatch proceeds into the signer path.
            const string KeywordSearchResponse =
                "{\"status\":\"ok\",\"result\":{\"items\":[{\"id\":1,\"hid\":\"testhid\",\"title\":\"Test\"}]}}";
            var keywordSearchHttpResponse = new HttpResponse(
                new HttpRequest("https://comix.to/api/v1/manga?keyword=Test"),
                new HttpHeader { ContentType = "application/json" },
                KeywordSearchResponse,
                HttpStatusCode.OK);
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.GetAsync(It.IsAny<HttpRequest>()))
                  .ReturnsAsync(keywordSearchHttpResponse);
        }

        [TearDown]
        public void TearDown()
        {
            LogManager.Configuration = _previousConfig;
        }

        [Test]
        public async Task Fetch_should_RecordFailure_when_signer_returns_decryptError_envelope()
        {
            // ARRANGE: SetUp wires Mock<IComixSigner> to return the envelope.
            var criteria = new MangaSearchCriteria
            {
                Manga = new NzbDrone.Core.Manga.Manga
                {
                    Title = "Test",
                    CleanTitle = "test"
                }
            };

            // ACT
            await Subject.Fetch(criteria);

            // ASSERT — Phase 17.2 D-3 / WR-GC-01: the envelope must route to RecordFailure on
            // the .NET caller side (ComixIndexer.DispatchSignerPathsAsync), NOT be silently
            // parsed to a null Result by the existing ParseResponse path.
            Mocker.GetMock<IIndexerSourceStatusService>()
                  .Verify(
                      s => s.RecordFailure("comix.to", It.IsAny<TimeSpan>()),
                      Times.AtLeastOnce(),
                      "Phase 17.2 D-3 / WR-GC-01: decryptError envelope from in-IIFE catch must route " +
                      "to RecordFailure on the .NET caller side, not be silently parsed to a null Result.");

            // T-17.2-11: the Warn line surfaces the decryptError STRING ONLY — never the
            // encrypted blob from the envelope's `e` field.
            var warnLines = _memoryTarget.Logs.Where(l => l.StartsWith("Warn|", StringComparison.Ordinal)).ToList();
            warnLines.Should().NotBeEmpty(
                "envelope detection must emit a Warn line documenting the routing.");
            foreach (var line in warnLines)
            {
                line.Should().NotContain(EncryptedBlobSentinelPrefix,
                    "T-17.2-11: the encrypted `e` blob MUST NOT appear in logged Warn output. " +
                    "Only the decryptError STRING is permitted in the log surface.");
            }
        }

        [Test]
        public async Task GetChapterPages_should_RecordFailure_when_signer_returns_decryptError_envelope()
        {
            // ARRANGE: a release fixture mirroring ComixGetChapterPagesFixture.BuildRelease() —
            // post-Phase-17.2 GAP-17-E DownloadUrl shape (no /pages suffix).
            var release = new ReleaseInfo
            {
                Title = "Test - Chapter 1 [en]",
                DownloadUrl = "https://comix.to/api/v1/chapters/9002242",
                ScanlationGroup = null
            };

            // ACT
            var manifest = await Subject.GetChapterPages(release);

            // ASSERT — Phase 17.2 D-3 / WR-GC-01: the envelope must route to RecordFailure on
            // the GetChapterPages path too.
            Mocker.GetMock<IIndexerSourceStatusService>()
                  .Verify(
                      s => s.RecordFailure("comix.to", It.IsAny<TimeSpan>()),
                      Times.AtLeastOnce(),
                      "Phase 17.2 D-3 / WR-GC-01: decryptError envelope on the pages path must " +
                      "also route to RecordFailure (mirrors DispatchSignerPathsAsync envelope-detect).");

            // CR-06 mitigation posture (preserved): GetChapterPages returns an empty manifest,
            // never null, on fail-soft paths.
            manifest.Should().NotBeNull();
            manifest.Pages.Should().BeEmpty(
                "envelope hit must return the same empty-manifest shape as the empty-payload " +
                "short-circuit (mirrors CR-06 fail-soft posture).");
            manifest.TotalCount.Should().Be(0);

            // T-17.2-11 blob-absence on the GetChapterPages path too.
            var warnLines = _memoryTarget.Logs.Where(l => l.StartsWith("Warn|", StringComparison.Ordinal)).ToList();
            warnLines.Should().NotBeEmpty(
                "envelope detection on the pages path must emit a Warn line documenting the routing.");
            foreach (var line in warnLines)
            {
                line.Should().NotContain(EncryptedBlobSentinelPrefix,
                    "T-17.2-11 (pages path): the encrypted `e` blob MUST NOT appear in logged Warn output.");
            }
        }

        [Test]
        public async Task Decrypt_envelope_log_message_should_NOT_contain_encrypted_blob_substring()
        {
            // T-17.2-11: dedicated assertion — the decryptError envelope MAY contain an
            // upstream-controlled encrypted payload (the `e` field, a base64-encoded cipher of
            // the full upstream API response). The Warn line emitted by the envelope-detection
            // routing surface MUST contain ONLY the `decryptError` STRING (the JS exception
            // text) — NEVER the `e` field's contents. Protects the source-status surface +
            // log sinks from unintentional payload leak.
            //
            // Implementation: NLog MemoryTarget capture (P-L-11 pattern) lets us inspect every
            // emitted log line. We assert blob-absence by greping for the 12-char prefix of the
            // sentinel `e` value defined at the top of this fixture.
            var criteria = new MangaSearchCriteria
            {
                Manga = new NzbDrone.Core.Manga.Manga
                {
                    Title = "Test",
                    CleanTitle = "test"
                }
            };

            await Subject.Fetch(criteria);

            // Capture ALL emitted log lines (Debug + Info + Warn + Error + Fatal — the layout
            // is "${level}|${message}").
            _memoryTarget.Logs.Should().NotBeEmpty(
                "envelope detection emits at least one log line (Warn) for the routing.");

            // Strict blob-absence: NO captured log line contains the sentinel prefix.
            var blobRegex = new Regex(Regex.Escape(EncryptedBlobSentinelPrefix));
            foreach (var line in _memoryTarget.Logs)
            {
                blobRegex.IsMatch(line).Should().BeFalse(
                    "T-17.2-11: log line MUST NOT contain any substring of the encrypted `e` blob — only the decryptError STRING is permitted in the log surface. Offending line: " + line);
            }

            // Defensive: also assert the decryptError STRING IS present somewhere — this
            // proves the envelope was detected + routed (rather than silently parsed away).
            _memoryTarget.Logs.Any(l => l.Contains("decryptError", StringComparison.OrdinalIgnoreCase))
                .Should().BeTrue(
                    "the routing log MUST surface the decryptError STRING so operators have a " +
                    "diagnostic anchor (the RecordFailure escalation alone surfaces only the " +
                    "SourceKey, not the failure cause).");
        }
    }
}
