using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentValidation.Results;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers.Http;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Indexers.Comix
{
    /// <summary>
    /// v1 reference port #1 — comix.to. Direct port from
    /// <c>keiyoushi/extensions-source/src/en/comix/Comix.kt</c> (Apache-2.0; PR #11658
    /// merged 2025-11-16; rebased against the live <c>/api/v1</c> shape on 2026-05-08
    /// during the comix-indexer-404 debug session).
    ///
    /// <para>
    /// Cloudflare posture: comix.to is Cloudflare-protected. UA override IS exposed via
    /// Settings UI (<see cref="ComixIndexerSettings.UserAgentOverride"/> has
    /// <c>[FieldDefinition]</c>) per RESEARCH.md anti-bot section — users may spoof a
    /// browser UA to dodge low-grade blocks. Persistent CF 403 → auto-disable via
    /// <see cref="IIndexerSourceStatusService"/> (D-17) + Health Check warning.
    /// </para>
    ///
    /// <para>
    /// Manga key resolution: comix.to keys manga by an opaque <c>hid</c>
    /// (e.g. <c>"mr3m0"</c>) — NOT a slug. The Manga model has no canonical hid field,
    /// so we resolve title → hid at search time by hitting
    /// <c>/api/v1/manga?keyword={title}</c> first and picking the top match. Future v2
    /// metadata-source linkage may persist the hid on the Manga model.
    /// </para>
    ///
    /// <para>
    /// Anti-bot token (Phase 17): <c>/api/v1/manga/{hid}/chapters</c> requires a
    /// <c>_=&lt;token&gt;</c> query parameter that comix.to rotates per-deploy via
    /// obfuscated browser-side JS. The runtime <see cref="IComixSigner"/> handles the
    /// signing inside an embedded headless Chromium page; both
    /// <see cref="Fetch(MangaSearchCriteria)"/> /
    /// <see cref="Fetch(ChapterSearchCriteria)"/> AND <see cref="GetChapterPages"/>
    /// dispatch through <see cref="IComixSigner.ProxyFetchAsync"/> per
    /// <see cref="IComixSigner"/> contract. See <c>.planning/debug/comix-invalid-token-403.md</c>
    /// for the 2026-05-10 root-cause record.
    /// </para>
    ///
    /// <para>
    /// Single-source convention: comix.to does NOT expose scanlation-group metadata on
    /// every chapter (only when the chapter is community-translated, vs <c>isOfficial=1</c>);
    /// parser populates <c>ReleaseInfo.ScanlationGroup</c> from the row's <c>group.name</c>
    /// when present. comix.to is English-only; parser sets
    /// <c>ReleaseInfo.TranslatedLanguage = "en"</c>.
    /// </para>
    ///
    /// <para>
    /// Phase 4's in-process downloader will consume
    /// <c>ReleaseInfo.DownloadUrl = https://comix.to/api/v1/chapters/{chapterId}/pages</c>
    /// (chapter manifest URL — NOT a single image) and call
    /// <see cref="GetDownloadHeaders(ReleaseInfo)"/> (returns
    /// <c>{"Referer": "https://comix.to/"}</c>) per D-14 — keiyoushi pattern.
    /// </para>
    /// </summary>
    public class ComixIndexer : HttpAggregatorBase<ComixIndexerSettings>
    {
        public override string Name => "Comix";
        public override DownloadProtocol Protocol => DownloadProtocol.Http;
        public override string DefaultSourceKey => "comix.to";

        private readonly IComixSigner _signer;

        public ComixIndexer(
            IHttpClient httpClient,
            IIndexerStatusService indexerStatusService,
            IIndexerSourceStatusService sourceStatusService,
            IConfigService configService,
            IMangaParsingService parsingService,
            IComixSigner signer,                              // NEW — Phase 17 D-05
            Logger logger,
            ILocalizationService localizationService)
            : base(httpClient, indexerStatusService, sourceStatusService, configService, parsingService, logger, localizationService)
        {
            _signer = signer;
        }

        public override IIndexerRequestGenerator GetRequestGenerator()
            => new ComixRequestGenerator { Settings = Settings, Signer = _signer };

        public override IParseIndexerResponse GetParser()
            => new ComixParser { BaseUrl = Settings.BaseUrl };

        // ── Phase 3 D-01 + Phase 17 D-05/D-08 — manga Fetch overloads via signer ────
        // HttpAggregatorBase narrows these to ABSTRACT (Plan 03-02 D-02), so we provide a
        // concrete implementation here. Phase 17 dispatch shape: resolve title → hid via
        // the (unsigned) search endpoint BEFORE asking the request generator to populate
        // ResolvedSignerPaths; loop those paths through _signer.ProxyFetchAsync to receive
        // decoded JSON; feed each JSON body to the parser.
        public override async Task<IList<ReleaseInfo>> Fetch(MangaSearchCriteria searchCriteria)
        {
            if (!SupportsSearch)
            {
                return Array.Empty<ReleaseInfo>();
            }

            var hid = await ResolveMangaHashAsync(searchCriteria?.Manga?.Title, searchCriteria?.Manga?.CleanTitle);
            if (string.IsNullOrWhiteSpace(hid))
            {
                _logger.Debug("Comix: no manga hid resolved for '{0}' — skipping fetch.", searchCriteria?.Manga?.Title);
                return Array.Empty<ReleaseInfo>();
            }

            // Sonarr divergence: Phase 17 D-05 — dispatch via ComixPuppeteerSigner, NOT
            // the legacy FetchReleases pipeline (Path A per RESEARCH N-2). Path A per RESEARCH N-2: the signer
            // returns the decoded JSON body so URL-with-token composition is dead.
            var crg = (ComixRequestGenerator)GetRequestGenerator();
            crg.ResolvedMangaHash = hid;
            crg.ResolvedMangaSlug = hid; // bare hid is sufficient; slug-form is optional metadata
            crg.GetSearchRequests(searchCriteria);   // populates ResolvedSignerPaths

            var releases = await DispatchSignerPathsAsync(crg).ConfigureAwait(false);
            return EnrichTitlesWithMangaName(releases, searchCriteria?.Manga?.Title);
        }

        public override async Task<IList<ReleaseInfo>> Fetch(ChapterSearchCriteria searchCriteria)
        {
            if (!SupportsSearch)
            {
                return Array.Empty<ReleaseInfo>();
            }

            var hid = await ResolveMangaHashAsync(searchCriteria?.Manga?.Title, searchCriteria?.Manga?.CleanTitle);
            if (string.IsNullOrWhiteSpace(hid))
            {
                _logger.Debug("Comix: no manga hid resolved for '{0}' — skipping fetch.", searchCriteria?.Manga?.Title);
                return Array.Empty<ReleaseInfo>();
            }

            // Sonarr divergence: Phase 17 D-05 — dispatch via ComixPuppeteerSigner, NOT
            // the legacy FetchReleases pipeline (Path A per RESEARCH N-2).
            var crg = (ComixRequestGenerator)GetRequestGenerator();
            crg.ResolvedMangaHash = hid;
            crg.ResolvedMangaSlug = hid;
            crg.GetSearchRequests(searchCriteria);   // populates ResolvedSignerPaths

            var releases = await DispatchSignerPathsAsync(crg).ConfigureAwait(false);
            return EnrichTitlesWithMangaName(releases, searchCriteria?.Manga?.Title);
        }

        /// <summary>
        /// Phase 17: loop <see cref="ComixRequestGenerator.ResolvedSignerPaths"/> through
        /// <see cref="IComixSigner.ProxyFetchAsync"/> and feed each decoded JSON body to
        /// <see cref="ComixParser"/>. Failures from the signer surface as Task throws
        /// (caller's responsibility — fail-soft happens at the SourceStatus boundary
        /// inside the signer, NOT here). On null/empty signer payload: return empty list.
        /// </summary>
        private async Task<IList<ReleaseInfo>> DispatchSignerPathsAsync(ComixRequestGenerator crg)
        {
            var allReleases = new List<ReleaseInfo>();
            var parser = GetParser();
            foreach (var apiPath in crg.ResolvedSignerPaths)
            {
                string json;
                try
                {
                    json = await _signer.ProxyFetchAsync(apiPath).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // WR-04 mitigation (revision iteration 2): rethrow user-initiated
                    // cancellation (shutdown, scheduler cancel) — do NOT swallow it as a
                    // "skipped path" because cancellation is structural, not a per-path
                    // failure. Wrapping it as a per-path skip lets a shutdown silently
                    // produce a partial release list while masking the cancellation root
                    // cause from the orchestrator.
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Comix: signer dispatch failed for '{0}'; skipping path.", apiPath);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(json))
                {
                    continue;
                }

                // Phase 17.2 D-3 / WR-GC-01 closure: the in-IIFE BRANCH-C catch in
                // ComixPuppeteerSigner.EvaluateProxyFetchAsync returns a
                // {result:null, e:..., decryptError:...} envelope on decrypt-throw.
                // JsonConvert.DeserializeObject<typed-shape>(envelope) parses to a
                // null-Result POCO — the existing parse path silently swallows the
                // failure (no log, no RecordFailure, no Health Check trip). Detect
                // the envelope here and route to RecordFailure so the per-SourceKey
                // 4-step escalation engages.
                // T-17.2-11: log ONLY the decryptError STRING — never the `e` field's
                // encrypted blob (cipher of the upstream API response).
                if (TryDetectDecryptErrorEnvelope(json, out var listDecryptError))
                {
                    _logger.Warn(
                        "Comix: signer returned decryptError envelope for '{0}'; routing to RecordFailure. decryptError={1}",
                        apiPath,
                        listDecryptError);
                    _sourceStatusService.RecordFailure(SourceKey);
                    continue;
                }

                var fakeRequest = new HttpRequest($"{Settings.BaseUrl.TrimEnd('/')}/api/v1{apiPath}");
                var fakeResponse = new HttpResponse(fakeRequest, new HttpHeader { ContentType = "application/json" }, json, System.Net.HttpStatusCode.OK);
                var indexerResponse = new IndexerResponse(new IndexerRequest(fakeRequest), fakeResponse);
                var parsed = parser.ParseResponse(indexerResponse);
                if (parsed != null)
                {
                    allReleases.AddRange(parsed);
                }
            }

            return allReleases;
        }

        /// <summary>
        /// Phase 17.2 D-3 / WR-GC-01: lightweight envelope detector for the in-IIFE
        /// BRANCH-C decrypt-throw shape (<c>{result:null, e:..., decryptError:...}</c>).
        /// Returns <c>true</c> + the decryptError STRING when the JSON has a non-empty
        /// <c>decryptError</c> string AND a null <c>result</c>; <c>false</c> otherwise
        /// (including malformed JSON — the legacy parse path handles those).
        /// </summary>
        private static bool TryDetectDecryptErrorEnvelope(string json, out string decryptError)
        {
            decryptError = null;
            try
            {
                var envelope = JsonConvert.DeserializeObject<JObject>(json);
                if (envelope == null)
                {
                    return false;
                }

                var decryptToken = envelope["decryptError"];
                if (decryptToken == null || decryptToken.Type != JTokenType.String)
                {
                    return false;
                }

                var resultToken = envelope["result"];
                if (resultToken != null && resultToken.Type != JTokenType.Null)
                {
                    return false;
                }

                var s = decryptToken.ToString();
                if (string.IsNullOrWhiteSpace(s))
                {
                    return false;
                }

                decryptError = s;
                return true;
            }
            catch (JsonReaderException)
            {
                // Malformed JSON — let the existing parse path handle it (it will
                // fail / produce empty list). Not our envelope shape.
                return false;
            }
        }

        /// <summary>
        /// comix.to's <c>/api/v1/manga/{hid}/chapters</c> endpoint returns chapter rows that
        /// do NOT carry the parent manga's title (the endpoint is keyed per-manga, so the
        /// title is implicit). MangaDownloadDecisionMaker extracts the manga title FROM the
        /// release title via <c>MangaParser.ParseChapterTitle</c> before resolving it against
        /// the database; without the manga name in the title, every release rejects with
        /// "Unknown Manga". Prefix the indexer-supplied manga title here — at the indexer
        /// layer where we still have the search criteria — so downstream parsing succeeds.
        /// Concurrency-safe: post-fetch transform on the returned list, no shared state.
        /// </summary>
        internal static IList<ReleaseInfo> EnrichTitlesWithMangaName(IList<ReleaseInfo> releases, string mangaTitle)
        {
            if (releases == null || string.IsNullOrWhiteSpace(mangaTitle))
            {
                return releases;
            }

            foreach (var r in releases)
            {
                if (r == null || string.IsNullOrWhiteSpace(r.Title))
                {
                    continue;
                }

                // Skip if the manga name is already present (e.g., manga-list path
                // ParseMangaList already prefixes via $"{m.Title} - Chapter ...").
                //
                // WR-05 mitigation (revision iteration 2): require a word-boundary
                // separator after the manga-title prefix so e.g. mangaTitle "Test"
                // does not silently absorb a release titled "Tested - Chapter 1"
                // (which would belong to a DIFFERENT manga). A bare StartsWith match
                // is a false-positive class that masks the bug downstream because
                // the chapter then lacks its true manga prefix and rejects with
                // "Unknown Manga" in MangaDownloadDecisionMaker.
                if (r.Title.Length > mangaTitle.Length
                    && r.Title.StartsWith(mangaTitle, StringComparison.OrdinalIgnoreCase)
                    && (r.Title[mangaTitle.Length] == ' '
                        || r.Title[mangaTitle.Length] == '-'
                        || r.Title[mangaTitle.Length] == ':'
                        || r.Title[mangaTitle.Length] == '_'))
                {
                    continue;
                }

                // Exact match (case-insensitive) is also already-prefixed.
                if (r.Title.Length == mangaTitle.Length
                    && r.Title.Equals(mangaTitle, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                r.Title = $"{mangaTitle} - {r.Title}";
            }

            return releases;
        }

        /// <summary>
        /// Look up a manga title against comix.to's <c>/api/v1/manga?keyword=</c> search
        /// endpoint and return the top-match's <c>hid</c>. Returns null when:
        /// <list type="bullet">
        /// <item>the title is empty,</item>
        /// <item>the search call fails (network or parse error — logged at Debug),</item>
        /// <item>the search returned zero items.</item>
        /// </list>
        /// On null return, the caller should produce an empty release list — comix.to has no
        /// match for this manga and that is a normal state.
        /// </summary>
        private async Task<string> ResolveMangaHashAsync(string title, string cleanTitle)
        {
            // Prefer the full title for keyword search (cleanTitle drops punctuation that
            // can change comix.to's tokenizer behavior). Fall back to cleanTitle if title
            // is empty.
            var keyword = !string.IsNullOrWhiteSpace(title) ? title : cleanTitle;
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return null;
            }

            try
            {
                // 2026-05-22+ cascade fix: comix.to fronts /api/v1/* behind Cloudflare —
                // plain GETs return 403 (see .planning/debug/comix-signer-rotation.md). The
                // keyword-search endpoint shares the same gate as /manga/{hid}/chapters and
                // /chapters/{id}, so route it through the env-module signer too. The signer
                // returns the unwrapped `result` object directly (bundle's ok+result
                // interceptor strips the `{status:"ok", result:{...}}` envelope) — we
                // accept both shapes for forward compatibility across rotations (matches
                // ComixParser pattern + ComixSignerLiveFixture.ExtractFirstChapterId).
                var apiPath = $"/manga?keyword={Uri.EscapeDataString(keyword)}&limit=10";
                var json = await _signer.ProxyFetchAsync(apiPath).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                var probe = JObject.Parse(json);
                var items = probe["items"] as JArray
                         ?? probe["result"]?["items"] as JArray;
                if (items == null || items.Count == 0)
                {
                    return null;
                }

                // Match preference (Phase 33 COMIX2-01 — Sonarr divergence: no Sonarr peer;
                // comix.to-specific keyword-search disambiguation):
                // comix.to's keyword search ranks a chapterless oneshot (hasChapters=false)
                // ahead of the canonical chaptered series for some titles — e.g. the English
                // altTitle "Komi Can't Communicate" returns the oneshot `e0nkm` (0 chapters)
                // before the 500-chapter romaji-titled "Komi-san wa Komyushou Desu." (`xkvvj`),
                // so an exact-title match on the English title fails and the pre-Phase-33
                // first-hit fallback picked the oneshot → an empty /manga/{hid}/chapters body.
                // Prefer entries that actually have chapters. Priority:
                //   (1) exact (case-insensitive) title match among chaptered entries,
                //   (2) first chaptered entry,
                //   (3) exact title match among all entries,
                //   (4) first entry — (3)+(4) preserve the pre-Phase-33 fallback so titles
                //       with no chaptered hit at all still degrade gracefully.
                bool HasChapters(JToken it) => it?["hasChapters"]?.Value<bool?>() == true;
                bool ExactTitle(JToken it) =>
                    string.Equals(it?["title"]?.ToString(), keyword, StringComparison.OrdinalIgnoreCase);

                string FirstHidWhere(Func<JToken, bool> predicate)
                {
                    foreach (var item in items)
                    {
                        if (!predicate(item))
                        {
                            continue;
                        }

                        var hid = item?["hid"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(hid))
                        {
                            return hid;
                        }
                    }

                    return null;
                }

                return FirstHidWhere(it => HasChapters(it) && ExactTitle(it))
                    ?? FirstHidWhere(HasChapters)
                    ?? FirstHidWhere(ExactTitle)
                    ?? items[0]?["hid"]?.ToString();
            }
            catch (OperationCanceledException)
            {
                // WR-04 mitigation (revision iteration 2): cancellation is structural —
                // never silently translate it into a "no manga found" return.
                throw;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Comix: title-to-hid resolution failed for '{0}'.", keyword);
                return null;
            }
        }

        // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — TV-shape Fetch overrides
        // (Single|Season|Daily|Anime|SpecialEpisodeSearchCriteria, etc.) stripped per Plan 15-10
        // IndexerSearch/Definitions DELETE. Manga overloads above are canonical.

        // ── Phase 3 D-14 — per-source HTTP headers for Phase 4 in-process downloader ───────
        // comix.to MANDATES Referer: https://comix.to/ on chapter requests per keiyoushi
        // headersBuilder() pattern. Phase 4's in-process downloader applies this to each
        // image GET via this hook.
        public override Dictionary<string, string> GetDownloadHeaders(ReleaseInfo release)
            => new Dictionary<string, string>
            {
                ["Referer"] = $"{Settings.BaseUrl.TrimEnd('/')}/"
            };

        // ── Phase 4 D-01 + Phase 17 D-08 + Phase 17.2 GAP-17-E — chapter-pages dereference ──
        // Sonarr divergence: Phase 17 D-08 + Phase 17.2 GAP-17-E (2026-05-10) — comix.to's
        // legacy pages-list endpoint /api/v1/chapters/{id}/pages was retired alongside
        // response-body encryption + per-deploy signer rotation. The bundle's signer
        // allowlist now rejects all /chapters/{id}/<suffix> shapes; the bare chapter
        // detail endpoint /api/v1/chapters/{id} returns 200 with the pages list embedded
        // under `result.pages.{baseUrl, items[]}`. ComixParser.ParseChapterList constructs
        // DownloadUrl = "{BaseUrl}/api/v1/chapters/{ch.Id}" (no /pages suffix) so the
        // consumer-side prefix-strip below naturally yields "/chapters/{id}" — the
        // survey-winner shape per 17.2-PAGES-ENDPOINT-SURVEY.md. The decoded body is
        // deserialized via ComixChapterPagesResponse (now chapter-detail-rooted under
        // .Result, with the convenience .Pages accessor composing
        // baseUrl + items[].url to absolute URLs). Path-only; CDN image GETs
        // (cdn.comix.to/.../*.jpg or wowpic-style host) below stay on plain IHttpClient
        // (only the manifest endpoint is signed).
        public override async Task<ChapterManifest> GetChapterPages(ReleaseInfo release)
        {
            // Strip /api/v1 prefix from the absolute URL so the signer signs the path
            // shape comix.to expects (per ComixRequestGenerator convention). Phase 17.2
            // GAP-17-E: yields /chapters/{id} (no /pages suffix) which is the survey-
            // winner shape the bundle's signer allowlist accepts post-2026-05-10.
            var apiPath = new Uri(release.DownloadUrl).AbsolutePath;
            if (apiPath.StartsWith("/api/v1", StringComparison.Ordinal))
            {
                apiPath = apiPath.Substring("/api/v1".Length);
            }

            var json = await _signer.ProxyFetchAsync(apiPath).ConfigureAwait(false);

            // CR-06 mitigation (revision iteration 2): the signer can legitimately
            // return null OR an empty/whitespace string (network 5xx swallowed at the
            // signer layer; comix.to returning an empty body for a stale chapter ID;
            // decryption-shim returning the raw envelope un-decoded if the response
            // shape changed). JsonConvert.DeserializeObject<>(null) returns null;
            // DeserializeObject<>("") throws JsonReaderException. Either way the
            // subsequent `resource.Pages` would NRE (or wrap a serializer exception
            // the Phase 4 download pipeline doesn't expect). Mirror DispatchSignerPathsAsync's
            // empty-payload posture: short-circuit with an empty manifest. The download
            // pipeline interprets `Pages.Count == 0` as "nothing to fetch" and surfaces
            // a Health Check warning rather than crashing.
            if (string.IsNullOrWhiteSpace(json))
            {
                _logger.Warn("Comix: GetChapterPages received empty signer payload for '{0}'; returning empty manifest.", apiPath);
                return new ChapterManifest
                {
                    Pages = Array.Empty<ChapterPage>(),
                    ScanlationGroup = release.ScanlationGroup,
                    TotalCount = 0,
                    ExpiresAt = null
                };
            }

            // Phase 17.2 D-3 / WR-GC-01 closure: same envelope-detect routing as
            // DispatchSignerPathsAsync — the in-IIFE BRANCH-C catch in
            // ComixPuppeteerSigner.EvaluateProxyFetchAsync emits the
            // {result:null, e:..., decryptError:...} envelope on decrypt-throw.
            // Without this guard, JsonConvert.DeserializeObject<ComixChapterPagesResponse>(envelope)
            // returns a Result with null pages — we silently produce a zero-page
            // manifest with no escalation. Fail-soft to empty manifest (CR-06
            // posture) AND route to RecordFailure so the 4-step escalation engages.
            // T-17.2-11: log ONLY the decryptError STRING — never the `e` blob.
            if (TryDetectDecryptErrorEnvelope(json, out var pagesDecryptError))
            {
                _logger.Warn(
                    "Comix: GetChapterPages signer returned decryptError envelope for '{0}'; routing to RecordFailure + returning empty manifest. decryptError={1}",
                    apiPath,
                    pagesDecryptError);
                _sourceStatusService.RecordFailure(SourceKey);
                return new ChapterManifest
                {
                    Pages = Array.Empty<ChapterPage>(),
                    ScanlationGroup = release.ScanlationGroup,
                    TotalCount = 0,
                    ExpiresAt = null
                };
            }

            var resource = JsonConvert.DeserializeObject<ComixChapterPagesResponse>(json);
            var pageUrls = resource?.Pages ?? new List<string>();
            var pages = new List<ChapterPage>(pageUrls.Count);
            for (var i = 0; i < pageUrls.Count; i++)
            {
                pages.Add(new ChapterPage
                {
                    Url = pageUrls[i],
                    PageIndex = i + 1,
                    ContentTypeHint = null
                });
            }

            return new ChapterManifest
            {
                Pages = pages,
                ScanlationGroup = release.ScanlationGroup,    // typically null for Comix; non-null for community translations
                TotalCount = pageUrls.Count,
                ExpiresAt = null                               // durable URLs
            };
        }

        protected override Task Test(List<ValidationFailure> failures)
        {
            // Lightweight Test() implementation — honors the abstract contract.
            // Real connectivity verification happens via the GH-Actions soak workflow
            // (Plan 03-06) + Wave 0 fixtures. Mirrors MangaDexIndexer posture.
            return Task.CompletedTask;
        }
    }
}
