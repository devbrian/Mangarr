using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Indexers.Comix
{
    // Sonarr divergence: no Sonarr peer. Phase 33 (COMIX2-01) introduces an offline-tier cassette
    // signer for comix.to (test-mode swap for ComixPuppeteerSigner — Comix anti-bot signing
    // cannot be HTTP-cassette'd at the IHttpClient layer per Phase 17 D-05 / Phase 18 D-11).
    // Mangarr-only seam; Pattern S2 / sonarr-consistency-audit Pattern ι allowlist coverage.
    //
    // <para>
    // Architecture (per Phase 33 CONTEXT.md D-02..D-05): mirrors the HTTP-layer
    // <see href="file:../../../NzbDrone.Automation.Test/TestKit/CassetteHandler.cs">CassetteHandler</see>
    // shape at the <see cref="IComixSigner"/> interface boundary. Keys cassettes by SHA-256-truncated
    // hash of <c>apiPath</c> (16 hex chars) so the cassette filename is deterministic + filesystem-safe.
    // Stores the DECODED JSON body (what <c>ProxyFetchAsync</c> returns post env-module-oracle
    // decryption per Phase 17 D-05 / 2026-05-23 env-module-oracle pivot) — comix.to anti-bot
    // rotation (24-72h cadence per Risk Register row 6) affects SIGNING (URL tokens / env-module
    // bundle URL), NOT the decoded response body shape, so cassettes don't go stale on rotation
    // (D-12). A sidecar <c>MANIFEST.json</c> maps <c>apiPath → filename</c> for human diffing
    // so reviewers don't need to cross-reference a hash to know what each cassette covers
    // (D-05).
    // </para>
    //
    // <para>
    // Mode semantics mirror CassetteHandler.cs:46-85 verbatim per D-03 / D-04:
    //   * <see cref="CassetteMode.Replay"/>: cassette file MUST exist; miss throws
    //     <see cref="InvalidOperationException"/> with the exact same shape as
    //     <c>CassetteHandler</c>'s miss message (D-04 — forces explicit recording, prevents
    //     silent gaps in CI).
    //   * <see cref="CassetteMode.Record"/>: always delegate to <c>_inner</c>
    //     (real <c>ComixPuppeteerSigner</c>); persist the returned body verbatim.
    //   * <see cref="CassetteMode.ReplayOrRecord"/>: read from disk if exists, otherwise
    //     delegate + persist (orchestrator-driven LIVE recording flow per D-07).
    // </para>

    /// <summary>
    /// Offline-tier <see cref="IComixSigner"/> impl that serves comix.to API responses
    /// from per-request JSON cassettes on disk. Test-mode swap for
    /// <see cref="ComixPuppeteerSigner"/> wired via env-var gate at
    /// <c>NzbDrone.Host/Startup.cs</c> per Phase 33 D-03 (env vars unset → production
    /// path resolves <c>ComixPuppeteerSigner</c> unconditionally).
    /// </summary>
    public class CassettingComixSigner : IComixSigner
    {
        private const string ComixSubdir = "Comix";
        private const string ManifestFileName = "MANIFEST.json";

        private static readonly JsonSerializerOptions ManifestSerializerOptions = new()
        {
            WriteIndented = true
        };

        private readonly string _cassetteDir;
        private readonly CassetteMode _mode;
        private readonly IComixSigner _inner;

        private readonly string _comixDir;
        private readonly string _manifestPath;

        // Serialize MANIFEST read-modify-write so concurrent Record / ReplayOrRecord writes
        // don't lose entries via last-writer-wins. Cassette file writes themselves are
        // per-apiPath so the SHA-256-trunc filename makes collisions vanishingly unlikely
        // (same apiPath → same filename → idempotent overwrite is desired Record-mode
        // semantics anyway). The lock specifically protects the MANIFEST.json sidecar.
        private readonly object _manifestLock = new();

        /// <summary>
        /// Construct the cassetting signer. Ensures the <c>{cassetteDir}/Comix/</c>
        /// subdirectory exists at construction so per-request writes don't pay an
        /// existence-probe per call.
        /// </summary>
        /// <param name="cassetteDir">
        /// Base cassette directory — the parent of the <c>Comix/</c> subdirectory
        /// (typically the value of <c>MANGARR_TEST_CASSETTE_DIR</c> env var, sibling
        /// to the HTTP-layer <c>MangaDex/</c> + <c>Komga/</c> cassette dirs).
        /// </param>
        /// <param name="mode">
        /// Cassette mode (<see cref="CassetteMode.Replay"/> / <see cref="CassetteMode.Record"/>
        /// / <see cref="CassetteMode.ReplayOrRecord"/>).
        /// </param>
        /// <param name="inner">
        /// Delegate signer used on <see cref="CassetteMode.Record"/> always and on
        /// <see cref="CassetteMode.ReplayOrRecord"/> miss. May be <c>null</c> when
        /// <paramref name="mode"/> is <see cref="CassetteMode.Replay"/> (Replay never
        /// delegates per D-04). Required for Record / ReplayOrRecord paths.
        /// </param>
        public CassettingComixSigner(string cassetteDir, CassetteMode mode, IComixSigner inner)
        {
            if (string.IsNullOrEmpty(cassetteDir))
            {
                throw new ArgumentException(
                    "CassettingComixSigner requires a non-empty cassetteDir " +
                    "(MANGARR_TEST_CASSETTE_DIR env var must be set).",
                    nameof(cassetteDir));
            }

            _cassetteDir = cassetteDir;
            _mode = mode;
            _inner = inner;

            _comixDir = Path.Combine(_cassetteDir, ComixSubdir);
            _manifestPath = Path.Combine(_comixDir, ManifestFileName);
            Directory.CreateDirectory(_comixDir);
        }

        /// <summary>
        /// Resolve the cassette for <paramref name="apiPath"/> per the configured
        /// <see cref="CassetteMode"/>. See class-level docs for full mode semantics
        /// (Phase 33 D-02..D-05).
        /// </summary>
        public async Task<string> ProxyFetchAsync(string apiPath, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(apiPath))
            {
                throw new ArgumentException(
                    "CassettingComixSigner.ProxyFetchAsync requires a non-empty apiPath.",
                    nameof(apiPath));
            }

            ct.ThrowIfCancellationRequested();

            var fileName = $"{ComputeKey(apiPath)}.json";
            var cassettePath = Path.Combine(_comixDir, fileName);
            var cassetteExists = File.Exists(cassettePath);

            if (_mode == CassetteMode.Replay)
            {
                if (!cassetteExists)
                {
                    // D-04: miss-message shape mirrors CassetteHandler.cs:50-54 verbatim
                    // ("Cassette miss: <key>\nExpected file: <path>\nRun with
                    // MANGARR_TEST_CASSETTE_MODE=Record to capture."). Forces explicit
                    // recording — prevents the cassetting signer silently returning empty
                    // or falling through to live comix.to in CI.
                    throw new InvalidOperationException(
                        $"Cassette miss: {apiPath}\n" +
                        $"Expected file: {cassettePath}\n" +
                        $"Run with MANGARR_TEST_CASSETTE_MODE=Record to capture.");
                }

                return await File.ReadAllTextAsync(cassettePath, ct).ConfigureAwait(false);
            }

            if (_mode == CassetteMode.ReplayOrRecord && cassetteExists)
            {
                return await File.ReadAllTextAsync(cassettePath, ct).ConfigureAwait(false);
            }

            // Record mode (or ReplayOrRecord miss): delegate to inner + persist.
            if (_inner == null)
            {
                throw new InvalidOperationException(
                    $"CassettingComixSigner in mode '{_mode}' requires a non-null inner " +
                    "signer to delegate Record / ReplayOrRecord-miss paths to. Pass the real " +
                    "ComixPuppeteerSigner instance via the constructor's 'inner' parameter " +
                    "(see NzbDrone.Host/Startup.cs registration site per Phase 33 D-03).");
            }

            var body = await _inner.ProxyFetchAsync(apiPath, ct).ConfigureAwait(false);

            await File.WriteAllTextAsync(cassettePath, body ?? string.Empty, ct).ConfigureAwait(false);
            UpdateManifest(apiPath, fileName);

            return body;
        }

        /// <summary>
        /// Compute the cassette filename key for an <paramref name="apiPath"/>. SHA-256 of
        /// the UTF-8 bytes, truncated to the first 16 hex characters (lowercase). Same
        /// shape as CassetteHandler.ComputeKey (CassetteHandler.cs:87-93) except the
        /// input is the apiPath verbatim instead of <c>method|url</c> — appropriate
        /// because <see cref="IComixSigner.ProxyFetchAsync"/> is keyed on apiPath alone.
        /// </summary>
        private static string ComputeKey(string apiPath)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(apiPath));
            return Convert.ToHexString(bytes).ToLowerInvariant().Substring(0, 16);
        }

        /// <summary>
        /// Read-modify-write the MANIFEST.json sidecar to map
        /// <paramref name="apiPath"/> → <paramref name="fileName"/>. Missing or unparseable
        /// MANIFEST is treated as <c>{}</c> so a corrupted or hand-edited sidecar self-heals
        /// on the next Record write rather than crashing the test run.
        /// </summary>
        private void UpdateManifest(string apiPath, string fileName)
        {
            lock (_manifestLock)
            {
                Dictionary<string, string> manifest = null;
                if (File.Exists(_manifestPath))
                {
                    try
                    {
                        var existing = File.ReadAllText(_manifestPath);
                        if (!string.IsNullOrWhiteSpace(existing))
                        {
                            manifest = JsonSerializer.Deserialize<Dictionary<string, string>>(existing);
                        }
                    }
                    catch (JsonException)
                    {
                        // Manifest corrupted (e.g. hand-edited during a recording session) —
                        // treat as empty + overwrite. Cassettes themselves are still on disk
                        // so the data loss is recoverable.
                        manifest = null;
                    }
                }

                manifest ??= new Dictionary<string, string>(StringComparer.Ordinal);
                manifest[apiPath] = fileName;

                var serialized = JsonSerializer.Serialize(manifest, ManifestSerializerOptions);
                File.WriteAllText(_manifestPath, serialized);
            }
        }
    }
}
