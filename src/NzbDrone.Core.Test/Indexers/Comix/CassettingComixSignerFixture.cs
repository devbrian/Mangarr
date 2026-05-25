using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Indexers.Comix;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.Comix
{
    /// <summary>
    /// Phase 33 (COMIX2-01) Plan 33-02 Task 1 — Round-trip + miss + replay-no-inner-call
    /// proofs for the new <see cref="CassettingComixSigner"/> seam (per D-02..D-05).
    ///
    /// <para>
    /// These three tests pin the contract: Record→Replay round-trip (cassette file
    /// + MANIFEST.json sidecar written in expected shape), Replay-miss throws
    /// <see cref="InvalidOperationException"/> with the same shape as the HTTP-layer
    /// <c>CassetteHandler</c> (TestKit/CassetteHandler.cs:50-54) per D-04, and
    /// ReplayOrRecord on existing cassette returns disk content WITHOUT invoking the
    /// inner signer (proves the cassette is consumed end-to-end and doesn't silently
    /// fall through to a real Chromium spawn in CI).
    /// </para>
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class CassettingComixSignerFixture : CoreTest
    {
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"comix-cassette-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (_tempDir != null && Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup; non-fatal.
            }
        }

        private static string ExpectedCassettePath(string baseDir, string apiPath)
        {
            // Mirror CassettingComixSigner.ComputeKey shape — SHA-256 truncated to first
            // 16 chars (lowercase hex). Must keep in lockstep with the implementation
            // (Behavior 3); these tests are the contract guard for that shape.
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(apiPath));
            var key = Convert.ToHexString(bytes).ToLowerInvariant().Substring(0, 16);
            return Path.Combine(baseDir, "Comix", $"{key}.json");
        }

        [Test]
        public async Task Record_then_Replay_writes_cassette_at_sha256_path_and_updates_manifest()
        {
            const string apiPath = "/manga/abc/chapters";
            const string body = "{\"status\":\"ok\"}";

            var inner = new Mock<IComixSigner>();
            inner.Setup(s => s.ProxyFetchAsync(apiPath, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(body);

            // Record phase — delegate to inner, write to disk.
            var recorder = new CassettingComixSigner(_tempDir, CassetteMode.Record, inner.Object);
            var recorded = await recorder.ProxyFetchAsync(apiPath, CancellationToken.None);

            recorded.Should().Be(body);

            var expectedPath = ExpectedCassettePath(_tempDir, apiPath);
            File.Exists(expectedPath).Should().BeTrue(
                $"Record mode should write the cassette at {expectedPath} (16-hex SHA-256 trunc)");
            var diskContent = await File.ReadAllTextAsync(expectedPath);
            diskContent.Should().Be(body, "cassette body is the verbatim decoded JSON returned by inner");

            var manifestPath = Path.Combine(_tempDir, "Comix", "MANIFEST.json");
            File.Exists(manifestPath).Should().BeTrue("MANIFEST.json sidecar must exist post-Record per D-05");
            var manifestJson = await File.ReadAllTextAsync(manifestPath);
            var manifest = JsonSerializer.Deserialize<Dictionary<string, string>>(manifestJson);
            manifest.Should().NotBeNull();
            manifest.Should().ContainKey(apiPath, "MANIFEST maps apiPath → filename per D-05");
            manifest[apiPath].Should().Be(Path.GetFileName(expectedPath),
                "manifest value is the cassette filename for human diffing");

            // Replay phase against the just-written cassette — verifies round-trip.
            var replayInner = new Mock<IComixSigner>();
            replayInner.Setup(s => s.ProxyFetchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                       .ThrowsAsync(new InvalidOperationException("Replay must NOT call inner"));
            var replayer = new CassettingComixSigner(_tempDir, CassetteMode.Replay, replayInner.Object);
            var replayed = await replayer.ProxyFetchAsync(apiPath, CancellationToken.None);

            replayed.Should().Be(body, "Replay mode must read the previously-recorded cassette body verbatim");
            replayInner.Verify(
                s => s.ProxyFetchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never,
                "Replay on hit must NOT delegate to inner");
        }

        [Test]
        public void Replay_miss_throws_shaped_InvalidOperationException()
        {
            const string apiPath = "/manga/missing/chapters";

            var inner = new Mock<IComixSigner>();
            inner.Setup(s => s.ProxyFetchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new InvalidOperationException("Replay mode must not delegate to inner"));

            var subject = new CassettingComixSigner(_tempDir, CassetteMode.Replay, inner.Object);

            Func<Task> act = () => subject.ProxyFetchAsync(apiPath, CancellationToken.None);

            act.Should().ThrowAsync<InvalidOperationException>()
               .Result.Which.Message
                .Should().Contain("Cassette miss: /manga/missing/chapters",
                                  "miss message must surface the apiPath verbatim per D-04 (mirrors CassetteHandler.cs:50-54)")
                .And.Contain("Run with MANGARR_TEST_CASSETTE_MODE=Record",
                             "miss message must direct the operator to the recording mode per D-04");

            inner.Verify(
                s => s.ProxyFetchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never,
                "Replay-miss must NOT delegate to inner — it must throw to force explicit recording per D-04");
        }

        [Test]
        public async Task ReplayOrRecord_on_existing_cassette_returns_disk_without_invoking_inner()
        {
            const string apiPath = "/chapters/12345";
            const string preRecordedBody = "{\"id\":\"12345\",\"pages\":{\"baseUrl\":\"https://cdn.test\",\"items\":[]}}";

            // Pre-populate a cassette at the expected path.
            var expectedPath = ExpectedCassettePath(_tempDir, apiPath);
            Directory.CreateDirectory(Path.GetDirectoryName(expectedPath));
            await File.WriteAllTextAsync(expectedPath, preRecordedBody);

            var inner = new Mock<IComixSigner>();
            inner.Setup(s => s.ProxyFetchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new InvalidOperationException("ReplayOrRecord on hit must not delegate to inner"));

            var subject = new CassettingComixSigner(_tempDir, CassetteMode.ReplayOrRecord, inner.Object);
            var result = await subject.ProxyFetchAsync(apiPath, CancellationToken.None);

            result.Should().Be(preRecordedBody,
                "ReplayOrRecord on existing cassette must serve disk content per D-02");
            inner.Verify(
                s => s.ProxyFetchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never,
                "ReplayOrRecord on hit must NOT delegate to inner");
        }
    }
}
