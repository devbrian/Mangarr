using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using NzbDrone.Core.Indexers.Gateway.Responses;
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
                    PublishDate = r.PublishDate,
                    Size = r.SizeBytes ?? 0,
                    DownloadProtocol = DownloadProtocol.Http,
                    ScanlationGroup = r.ScanlationGroup,
                    TranslatedLanguage = r.Language
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
            if (r.MangaTitle == null || r.ChapterNumber == null)
            {
                return r.Title;
            }

            // The "0.###" invariant format is load-bearing (Pitfall 3): 179.0 → "179" (no trailing
            // .0), 12.5 → "12.5", 1.123 → "1.123" (no precision loss). Pinned against the
            // MangaParser.ParseChapterTitle grammar by the Task-1 round-trip fixtures (A1).
            var t = $"{r.MangaTitle} - Chapter {r.ChapterNumber.Value.ToString("0.###", CultureInfo.InvariantCulture)}";

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

            return t;
        }
    }
}
