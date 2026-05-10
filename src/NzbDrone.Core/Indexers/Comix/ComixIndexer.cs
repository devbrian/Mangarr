using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentValidation.Results;
using Newtonsoft.Json;
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
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Comix: signer dispatch failed for '{0}'; skipping path.", apiPath);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(json))
                {
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
                if (r.Title.StartsWith(mangaTitle, StringComparison.OrdinalIgnoreCase))
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
                var generator = (ComixRequestGenerator)GetRequestGenerator();
                var url = generator.BuildSearchUrl(keyword);
                var request = new HttpRequest(url, HttpAccept.Json);
                request.Headers["Referer"] = $"{Settings.BaseUrl.TrimEnd('/')}/";
                request.RateLimitKey = SourceKey;
                request.RateLimit = RateLimit;
                request.Headers["User-Agent"] = ResolveUserAgent();

                var response = await _httpClient.GetAsync(request);
                if (string.IsNullOrWhiteSpace(response?.Content))
                {
                    return null;
                }

                var envelope = JsonConvert.DeserializeObject<ComixMangaListResponse>(response.Content);
                if (envelope?.Result?.Items == null || envelope.Result.Items.Count == 0)
                {
                    return null;
                }

                // Match preference: exact (case-insensitive) title match if available, else
                // first hit. comix.to's keyword search occasionally returns near-misses
                // (sub-string hits) ahead of the exact title — guard against that.
                foreach (var item in envelope.Result.Items)
                {
                    if (item != null && !string.IsNullOrWhiteSpace(item.Hid)
                        && string.Equals(item.Title, keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        return item.Hid;
                    }
                }

                return envelope.Result.Items[0]?.Hid;
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

        // ── Phase 4 D-01 + Phase 17 D-08 — Comix /api/v1/chapters/{id}/pages dereference ──
        // Sonarr divergence: Phase 17 D-08 — comix.to /api/v1/chapters/{id}/pages requires
        // the runtime signer (per RESEARCH.md N-3 + keiyoushi Signer.kt:238-241). Path-only;
        // CDN image GETs (cdn.comix.to/.../*.jpg) below stay on plain IHttpClient (the
        // image bytes themselves are not signed — only the manifest endpoint is).
        public override async Task<ChapterManifest> GetChapterPages(ReleaseInfo release)
        {
            // Strip /api/v1 prefix from the absolute URL so the signer signs the path
            // shape comix.to expects (per ComixRequestGenerator convention).
            var apiPath = new Uri(release.DownloadUrl).AbsolutePath;
            if (apiPath.StartsWith("/api/v1", StringComparison.Ordinal))
            {
                apiPath = apiPath.Substring("/api/v1".Length);
            }

            var json = await _signer.ProxyFetchAsync(apiPath).ConfigureAwait(false);
            var resource = JsonConvert.DeserializeObject<ComixChapterPagesResponse>(json);
            var pageUrls = resource.Pages;
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
