using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using NzbDrone.Common.Http;
using NzbDrone.Core.Indexers.Exceptions;
using NzbDrone.Core.Indexers.Gateway.Responses;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Indexers.Gateway
{
    /// <summary>
    /// JSON response parser for the manga gateway's <c>POST /search</c> + <c>GET /recent</c>
    /// endpoints (both return the FROZEN OpenAPI <c>ReleaseListResponse</c> shape). Maps
    /// <see cref="GatewayRelease"/>[] → <see cref="ReleaseInfo"/>[] and records each per-source
    /// <c>warnings[]</c> entry via <see cref="IIndexerSourceStatusService.RecordFailure(string, System.TimeSpan)"/>
    /// WITHOUT ever throwing (D-03a / GWIX-04 — one degraded source must not fail the whole search).
    ///
    /// <para>
    /// Unlike the analog parsers (<c>MangaDexParser</c> / <c>ComixParser</c>) which are parameterless,
    /// this parser takes <see cref="IIndexerSourceStatusService"/> so it can escalate per-source
    /// failure pressure from runtime <c>warnings[]</c>. The indexer injects it in Plan 04.
    /// </para>
    ///
    /// <para>
    /// Guid de-dup is NOT performed here — the kept <c>IndexerBase.CleanupReleases</c> engine does
    /// <c>DistinctBy(Guid)</c> + identity-stamping when the indexer drives Fetch (Plan 04).
    /// </para>
    /// </summary>
    public class GatewayParser : IParseIndexerResponse
    {
        private readonly IIndexerSourceStatusService _sourceStatusService;

        public GatewayParser(IIndexerSourceStatusService sourceStatusService)
        {
            _sourceStatusService = sourceStatusService;
        }

        public IList<ReleaseInfo> ParseResponse(IndexerResponse indexerResponse)
        {
            var releases = new List<ReleaseInfo>();

            var content = indexerResponse?.HttpResponse?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                return releases;
            }

            // Parity with the caps CR-02 fix: a 2xx body can carry a top-level error envelope
            // ({"error":{"code":"auth"}}) — the gateway 200-wraps application errors. Run the kept
            // error-code ladder on the body BEFORE treating it as a release list, otherwise an
            // auth/rate-limit failure deserializes into a non-null response with an empty Releases
            // list and is silently recorded as "0 releases" success. This is distinct from a
            // per-source warnings[] entry (D-03a, handled below without throwing) — a top-level
            // error means the whole request failed.
            ThrowForErrorEnvelope(content, indexerResponse);

            var response = JsonConvert.DeserializeObject<GatewaySearchResponse>(content);
            if (response == null)
            {
                return releases;
            }

            foreach (var r in response.Releases ?? Enumerable.Empty<GatewayRelease>())
            {
                if (r == null)
                {
                    continue;
                }

                releases.Add(new ReleaseInfo
                {
                    Guid = r.Guid,
                    Title = BuildTitle(r),

                    // Opaque R6 token — NOT a URL Mangarr GETs (Pitfall 5 / SSRF). It is submitted
                    // BACK to the gateway's POST /downloads in Phase 38; never dereferenced here.
                    DownloadUrl = r.DownloadHandle,
                    InfoUrl = r.InfoUrl,

                    // WR-03: substitute "now" for a missing/null wire publishDate rather than
                    // emitting DateTime.MinValue (which would make a brand-new release look ancient
                    // to age-based decision specs / RSS watermark dedup).
                    // ToUniversalTime() normalizes Kind to Utc: Newtonsoft's default RoundtripKind
                    // yields Kind=Local for an offset-style wire date (e.g. "...+00:00", which the
                    // live gateway sends), and the engine treats ReleaseInfo.PublishDate as UTC.
                    PublishDate = (r.PublishDate ?? DateTime.UtcNow).ToUniversalTime(),
                    Size = r.SizeBytes ?? 0,
                    DownloadProtocol = DownloadProtocol.Http,
                    ScanlationGroup = r.ScanlationGroup,
                    TranslatedLanguage = r.Language,

                    // GWIX: the gateway is a single host (Indexer is stamped to the gateway's
                    // display name by CleanupReleases), but each release names its upstream
                    // SourceKey (mangadex/comix/…). Surface it via the kept ReleaseInfo.Source
                    // field so the InteractiveSearch Indexer column can show which source a
                    // release actually came from. Survives CleanupReleases (which only stamps
                    // Indexer/IndexerId/Protocol/Priority).
                    Source = r.SourceKey,

                    // D-06 (RECON-04): thread the cross-source ID dict (mangadexId/anilistId/malId)
                    // through verbatim so the Plan-03 synthesis attribution gate can ID-match first.
                    // Passed UNCHANGED — no transform/filter here; defensive value parsing
                    // (TryGuid/TryInt, never throw) lives in the synthesis service (T-40-05). A null
                    // dict stays null so the gate falls through to exact-title (D-06 ordering).
                    // Survives CleanupReleases the same way Source does (T-40-06).
                    Ids = r.Ids
                });
            }

            // D-03a: each per-source warning escalates that SourceKey's failure pressure via the
            // kept IIndexerSourceStatusService. This NEVER throws and NEVER suppresses the good
            // releases above (GWIX-04 isolation). An enabled:false source is filtered upstream in
            // Plan 02's request generator (D-03 skip-only) and never reaches a warnings[] entry, so
            // RecordFailure is never called for a disabled source.
            foreach (var w in response.Warnings ?? Enumerable.Empty<GatewaySourceWarning>())
            {
                if (w?.SourceKey != null)
                {
                    _sourceStatusService.RecordFailure(w.SourceKey);
                }
            }

            return releases;
        }

        // A 200 (or any) /search /recent body can wrap a top-level error envelope. Route auth →
        // ApiKeyException, rate_limited → TooManyRequestsException, anything else with an error.code
        // → IndexerException (never a swallowed empty release list). A normal success body has no
        // top-level `error`, so GatewayError.Error is null and this is a no-op. Mirrors the kept
        // GatewayCapabilitiesProvider ladder (A2).
        private static void ThrowForErrorEnvelope(string content, IndexerResponse indexerResponse)
        {
            string code;
            try
            {
                code = JsonConvert.DeserializeObject<GatewayError>(content)?.Error?.Code;
            }
            catch (JsonException)
            {
                // Not a parseable error envelope — let the normal release parse proceed.
                return;
            }

            if (string.IsNullOrEmpty(code))
            {
                return;
            }

            if (code == "auth")
            {
                throw new ApiKeyException("Gateway authentication failed");
            }

            if (code == "rate_limited")
            {
                throw new TooManyRequestsException(indexerResponse.HttpRequest, indexerResponse.HttpResponse);
            }

            throw new IndexerException(indexerResponse, "Gateway search request failed: {0}", code);
        }

        // Sonarr divergence: (Pattern-S2) — this is the ONE intentional Phase-37 divergence (D-02).
        // The frozen gateway contract (spike §6) says "trust the title" — emit release.title verbatim.
        // Mangarr instead RECONSTRUCTS a parser-canonical title from the structured hints
        // (mangaTitle + chapterNumber [+ language + scanlationGroup]) WHEN BOTH are present, so the
        // downstream MangaParser.ParseChapterTitle grammar resolves the right manga + chapter (A1).
        // Guardrail D-02b: when EITHER hint is null the verbatim gateway title is returned UNCHANGED
        // and the release is NEVER dropped (protects the InteractiveSearch UI-visible win).
        // Provenance: CONTEXT.md D-02 / D-02a / D-02b. See DIVERGENCE.md Phase 37 entry.
        private static string BuildTitle(GatewayRelease r)
        {
            // D-02b mandatory verbatim fallback — never drop a release that lacks complete hints.
            // IN-03: coalesce a null wire title to string.Empty so a null `title` never NREs
            // downstream parsing/decisioning.
            if (r.MangaTitle == null || r.ChapterNumber == null)
            {
                return r.Title ?? string.Empty;
            }

            // The "0.###" invariant format is load-bearing (Pitfall 3): 179.0 → "179" (no trailing
            // .0), 12.5 → "12.5", 1.123 → "1.123" (no precision loss). Pinned against the
            // MangaParser.ParseChapterTitle grammar by the Task-1 round-trip fixtures (A1).
            var chapterToken = r.ChapterNumber.Value.ToString("0.###", CultureInfo.InvariantCulture);
            var t = $"{r.MangaTitle} - Chapter {chapterToken}";

            // MangaLanguageParser reads a trailing [xx] tag.
            if (!string.IsNullOrWhiteSpace(r.Language))
            {
                t += $" [{r.Language}]";
            }

            // MangaScanlationGroupParser extracts the group from a LEADING ^[Group] bracket
            // (NOT the trailing position MangaDexParser uses — the grammar survey requires leading).
            if (!string.IsNullOrWhiteSpace(r.ScanlationGroup))
            {
                t = $"[{r.ScanlationGroup}] {t}";
            }

            // WR-04: D-02 reconstruction is only safe if it ROUND-TRIPS. An adversarial mangaTitle
            // (one already containing a "[bracket]", an embedded " - Chapter ", or a trailing
            // language-looking "[en]" tag) would make MangaParser.ParseChapterTitle extract the WRONG
            // manga title / chapter number — silently routing the release to the wrong manga. Assert
            // the reconstruction parses back to the expected mangaTitle AND chapter; if it does not,
            // fall back to the verbatim gateway title (D-02b semantics) rather than dropping it.
            var parsed = MangaParser.ParseChapterTitle(t);
            if (parsed == null ||
                !string.Equals(parsed.MangaTitle, r.MangaTitle, StringComparison.Ordinal) ||
                parsed.ChapterNumbers == null ||
                !parsed.ChapterNumbers.Contains(r.ChapterNumber.Value))
            {
                return r.Title ?? string.Empty;
            }

            return t;
        }
    }
}
