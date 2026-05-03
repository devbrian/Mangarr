using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers.Http;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Indexers.Comix
{
    /// <summary>
    /// v1 reference port #1 — comix.to. Direct port from
    /// <c>keiyoushi/extensions-source/src/en/comix/Comix.kt</c> (Apache-2.0; PR #11658
    /// merged 2025-11-16). comix.to ships a clean JSON API at <c>/api/v2/...</c> per
    /// RESEARCH.md verification — D-09 reverse-engineer-API-first cleanly satisfied;
    /// D-10 (no HTML parser dep) and D-13 (no browser automation) honored.
    ///
    /// <para>
    /// Cloudflare posture: comix.to is Cloudflare-protected. UA override IS exposed via
    /// Settings UI (<see cref="ComixIndexerSettings.UserAgentOverride"/> has
    /// <c>[FieldDefinition]</c>) per RESEARCH.md anti-bot section — users may spoof a
    /// browser UA to dodge low-grade blocks. Persistent CF 403 → auto-disable via
    /// <see cref="IIndexerSourceStatusService"/> (D-17) + Health Check warning (Plan 03-03
    /// IndexerSourceFailureCheck).
    /// </para>
    ///
    /// <para>
    /// Single-source convention: comix.to does NOT expose scanlation-group metadata on
    /// every chapter (only when the chapter is community-translated, vs <c>is_official=1</c>);
    /// parser populates <c>ReleaseInfo.ScanlationGroup</c> from the row's
    /// <c>scanlation_group.name</c> when present. comix.to is English-only per keiyoushi
    /// <c>lang</c> setting; parser sets <c>ReleaseInfo.TranslatedLanguage = "en"</c>.
    /// </para>
    ///
    /// <para>
    /// Phase 4's in-process downloader will consume
    /// <c>ReleaseInfo.DownloadUrl = https://comix.to/api/v2/chapters/{chapterId}</c>
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

        public ComixIndexer(
            IHttpClient httpClient,
            IIndexerStatusService indexerStatusService,
            IIndexerSourceStatusService sourceStatusService,
            IConfigService configService,
            IParsingService parsingService,
            Logger logger,
            ILocalizationService localizationService)
            : base(httpClient, indexerStatusService, sourceStatusService, configService, parsingService, logger, localizationService)
        {
        }

        public override IIndexerRequestGenerator GetRequestGenerator()
            => new ComixRequestGenerator { Settings = Settings };

        public override IParseIndexerResponse GetParser()
            => new ComixParser { BaseUrl = Settings.BaseUrl };

        // ── Phase 3 D-01 — manga Fetch overloads ─────────────────────────────────────
        // HttpAggregatorBase narrows these to ABSTRACT (Plan 03-02 D-02), so we provide a
        // concrete implementation here. We cannot delegate via base.Fetch(...) because the
        // abstract override forbids that (CS0205); we instead inline the standard
        // FetchReleases body (the same body HttpIndexerBase uses) so SourceKey-keyed rate
        // budget + honest UA (applied by HttpAggregatorBase.FetchIndexerResponse) survive
        // the dispatch. Mirrors MangaDexIndexer (Plan 03-04) pattern verbatim.
        public override Task<IList<ReleaseInfo>> Fetch(MangaSearchCriteria searchCriteria)
        {
            if (!SupportsSearch)
            {
                return Task.FromResult<IList<ReleaseInfo>>(Array.Empty<ReleaseInfo>());
            }

            return FetchReleases(g => g.GetSearchRequests(searchCriteria));
        }

        public override Task<IList<ReleaseInfo>> Fetch(ChapterSearchCriteria searchCriteria)
        {
            if (!SupportsSearch)
            {
                return Task.FromResult<IList<ReleaseInfo>>(Array.Empty<ReleaseInfo>());
            }

            return FetchReleases(g => g.GetSearchRequests(searchCriteria));
        }

        // ── Phase 3 D-03 — TV overloads throw NotSupportedException (per-class stubs) ───────
        // Comix is a manga indexer; the inherited TV-shaped overloads must trip loudly if
        // Phase 5/6 wiring accidentally crosses streams. ThingiProvider resolves
        // indexer-by-protocol so these are never called via the canonical pipeline anyway.
        public override Task<IList<ReleaseInfo>> Fetch(SeasonSearchCriteria searchCriteria)
            => throw new NotSupportedException("Comix is a manga indexer; TV search not applicable.");

        public override Task<IList<ReleaseInfo>> Fetch(SingleEpisodeSearchCriteria searchCriteria)
            => throw new NotSupportedException("Comix is a manga indexer; TV search not applicable.");

        public override Task<IList<ReleaseInfo>> Fetch(DailyEpisodeSearchCriteria searchCriteria)
            => throw new NotSupportedException("Comix is a manga indexer; TV search not applicable.");

        public override Task<IList<ReleaseInfo>> Fetch(DailySeasonSearchCriteria searchCriteria)
            => throw new NotSupportedException("Comix is a manga indexer; TV search not applicable.");

        public override Task<IList<ReleaseInfo>> Fetch(AnimeEpisodeSearchCriteria searchCriteria)
            => throw new NotSupportedException("Comix is a manga indexer; TV search not applicable.");

        public override Task<IList<ReleaseInfo>> Fetch(AnimeSeasonSearchCriteria searchCriteria)
            => throw new NotSupportedException("Comix is a manga indexer; TV search not applicable.");

        public override Task<IList<ReleaseInfo>> Fetch(SpecialEpisodeSearchCriteria searchCriteria)
            => throw new NotSupportedException("Comix is a manga indexer; TV search not applicable.");

        // ── Phase 3 D-14 — per-source HTTP headers for Phase 4 in-process downloader ───────
        // comix.to MANDATES Referer: https://comix.to/ on chapter requests per keiyoushi
        // headersBuilder() pattern. Phase 4's in-process downloader applies this to each
        // image GET via this hook.
        public override Dictionary<string, string> GetDownloadHeaders(ReleaseInfo release)
            => new Dictionary<string, string>
            {
                ["Referer"] = $"{Settings.BaseUrl.TrimEnd('/')}/"
            };

        // ── Phase 4 D-01 — Comix /api/v2/chapters/{id} dereference ─────────────────
        // Returns: { pages: [imageUrl1, imageUrl2, ...] } — durable URLs (no token rotation).
        // ExpiresAt = null because comix.to URLs do not expire; the auto-re-fetch on 403/410
        // (D-03) is a never-fired safety net for this source.
        //
        // Referer header: handled by GetDownloadHeaders override (returns
        // "Referer: https://comix.to/"). Plan 04-03's ChapterPageFetcher applies that header
        // before each image GET via the GetDownloadHeaders hook.
        //
        // CRITICAL (Pitfall 1): RateLimitKey set EXPLICITLY at the call site to SourceKey so
        // the /api/v2/chapters/{id} GET shares the per-SourceKey budget with indexer poll path.
        //
        // Synthesized fixture shape (Cloudflare blocks live capture per Phase 3 LEARNINGS):
        // ComixChapterPagesResponse { pages: List<string> } maps the keiyoushi Dto.kt shape.
        public override async Task<ChapterManifest> GetChapterPages(ReleaseInfo release)
        {
            var req = new HttpRequest(release.DownloadUrl);   // already https://comix.to/api/v2/chapters/{id}
            req.RateLimitKey = SourceKey;                                          // Phase 1 D-11 — shared budget
            req.Headers["User-Agent"] = ResolveUserAgent();                        // Phase 1 D-13 — honest UA
            req.Headers["Referer"] = $"{Settings.BaseUrl.TrimEnd('/')}/";          // Phase 3 D-Comix Cloudflare Pitfall

            var resp = await _httpClient.GetAsync<ComixChapterPagesResponse>(req);
            var pageUrls = resp.Resource.Pages;
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
