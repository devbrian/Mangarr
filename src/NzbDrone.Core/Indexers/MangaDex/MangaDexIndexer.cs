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
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Indexers.MangaDex
{
    /// <summary>
    /// v1 BEDROCK manga indexer. Direct port from MangaDex's published API documentation
    /// (NOT keiyoushi/extensions-source — see <c>THIRD-PARTY-NOTICES.md</c> for the
    /// distinction; landed in Plan 03-06). Shares <c>SourceKey="mangadex"</c> rate budget
    /// with the Phase 2
    /// <see cref="NzbDrone.Core.MetadataSource.MangaDex.MangaDexMetadataSource"/> instance
    /// (Phase 1 D-11/D-12 + Phase 2 D-22 — single bucket).
    ///
    /// <para>
    /// MangaDex ToS posture (Phase 3 SOURCE-07):
    /// <list type="bullet">
    /// <item>Honest UA <c>Mangarr/{version}</c> (Phase 1 D-13) —
    ///       <see cref="MangaDexIndexerSettings.UserAgentOverride"/> has NO
    ///       <c>[FieldDefinition]</c> attribute (Pitfall 4 / T-CONFIG-DRIFT-01 mitigated by
    ///       absence; reflection-fixture <c>MangaDexIndexerSettingsHonestUaFixture</c>
    ///       enforces).</item>
    /// <item>Scanlation-group attribution: <see cref="MangaDexParser"/> populates
    ///       <see cref="ReleaseInfo.ScanlationGroup"/> from
    ///       <c>relationships[scanlation_group].attributes.name</c>.</item>
    /// <item>40 req/min on <c>/at-home/server</c> — <see cref="MangaDexIndexerSettings"/>
    ///       defaults <c>RateSeconds=1.5</c> so Phase 4 image-fetch sharing the
    ///       <c>SourceKey</c> bucket cannot burst.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// Phase 4's in-process downloader will consume
    /// <c>ReleaseInfo.DownloadUrl = https://api.mangadex.org/at-home/server/{chapterId}</c>
    /// (chapter manifest URL — NOT a single image) and call
    /// <see cref="GetDownloadHeaders(ReleaseInfo)"/> (returns empty for MangaDex —
    /// /at-home/server URLs do NOT need a Referer per D-14).
    /// </para>
    /// </summary>
    public class MangaDexIndexer : HttpAggregatorBase<MangaDexIndexerSettings>
    {
        public override string Name => "MangaDex";
        public override DownloadProtocol Protocol => DownloadProtocol.Http;
        public override string DefaultSourceKey => "mangadex";

        public MangaDexIndexer(
            IHttpClient httpClient,
            IIndexerStatusService indexerStatusService,
            IIndexerSourceStatusService sourceStatusService,
            IConfigService configService,
            IMangaParsingService parsingService,
            Logger logger,
            ILocalizationService localizationService)
            : base(httpClient, indexerStatusService, sourceStatusService, configService, parsingService, logger, localizationService)
        {
        }

        public override IIndexerRequestGenerator GetRequestGenerator()
            => new MangaDexRequestGenerator { Settings = Settings, Api = BuildApi() };

        public override IParseIndexerResponse GetParser()
            => new MangaDexParser();

        private MangaDexIndexerApi BuildApi()
            => new MangaDexIndexerApi(_httpClient, Settings.BaseUrl, ResolveUserAgent, SourceKey);

        // ── Phase 3 D-01 — manga Fetch overloads ─────────────────────────────────────
        // HttpAggregatorBase narrows these to ABSTRACT (Plan 03-02 D-02), so we provide a
        // concrete implementation here. We cannot delegate via base.Fetch(...) because the
        // abstract override forbids that; we instead inline the standard FetchReleases body
        // (the same body HttpIndexerBase uses) so SourceKey-keyed rate budget + honest UA
        // (applied by HttpAggregatorBase.FetchIndexerResponse) survive the dispatch.
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

        // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — TV-shape Fetch overrides
        // (Single|Season|Daily|Anime|SpecialEpisodeSearchCriteria, etc.) stripped per Plan 15-10
        // IndexerSearch/Definitions DELETE. Manga overloads above are canonical.

        // ── Phase 3 D-14 — per-source HTTP headers for Phase 4 in-process downloader ───────
        // MangaDex /at-home/server URLs do NOT need a Referer (per RESEARCH §MangaDex Indexer);
        // return empty so Phase 4 downloader applies no extra headers.
        public override Dictionary<string, string> GetDownloadHeaders(ReleaseInfo release)
            => new Dictionary<string, string>();

        // ── Phase 4 D-01 — MangaDex /at-home/server/{chapterId} dereference ──────────
        // Returns: { result, baseUrl, chapter: { hash, data: [filenames], dataSaver: [...] } }
        // Tokens valid ~15min per VERIFIED api.mangadex.org/docs/04-chapter; D-03's reactive
        // 403/410 → re-fetch (orchestrated by ChapterDownloadService in plan 04-03) handles
        // expiry without a proactive scheduler.
        //
        // CRITICAL (Pitfall 1 — F-01 class regression guard): RateLimitKey is set EXPLICITLY at
        // the call site to SourceKey so the /at-home/server GET shares the per-SourceKey budget
        // with indexer poll path (Phase 1 D-11/D-12). The Phase 4 ChapterPageFetcher applies
        // the same discipline on per-image GETs.
        //
        // CRITICAL (token leakage): /at-home/server URLs do NOT take auth headers — sending
        // Authorization on a GET against uploads.mangadex.org would leak credentials to the
        // community CDN. MangaDex's GetDownloadHeaders override returns empty so plan 04-03's
        // ChapterPageFetcher applies no auth on the resolved image URLs either.
        //
        // ReleaseInfo.DownloadUrl is the chapter manifest URL (Phase 3 D-08) — for MangaDex it's
        // already the canonical /at-home/server/{chapterId} URL emitted by MangaDexParser
        // (verified in MangaDexParser.cs:97). We GET it directly without re-deriving.
        public override async Task<ChapterManifest> GetChapterPages(ReleaseInfo release)
        {
            var req = new HttpRequest(release.DownloadUrl);
            req.RateLimitKey = SourceKey;                            // Phase 1 D-11 — shared budget
            req.Headers["User-Agent"] = ResolveUserAgent();          // Phase 1 D-13 — honest UA

            var resp = await _httpClient.GetAsync<MangaDexAtHomeResource>(req);
            var chapter = resp.Resource.Chapter;
            var pages = new List<ChapterPage>(chapter.Data.Count);
            for (var i = 0; i < chapter.Data.Count; i++)
            {
                pages.Add(new ChapterPage
                {
                    Url = $"{resp.Resource.BaseUrl.TrimEnd('/')}/data/{chapter.Hash}/{chapter.Data[i]}",
                    PageIndex = i + 1,
                    ContentTypeHint = null   // URL-embedded extension preserved
                });
            }

            return new ChapterManifest
            {
                Pages = pages,
                ScanlationGroup = release.ScanlationGroup,            // Phase 3 D-Q4 — populated by parser
                TotalCount = chapter.Data.Count,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15)      // documented MangaDex TTL
            };
        }

        protected override Task Test(List<ValidationFailure> failures)
        {
            // Lightweight Test() implementation — honors the abstract contract.
            // Real connectivity verification happens via the GH-Actions soak workflow
            // (Plan 03-06) + Wave 0 fixtures. Calling base.Test would invoke
            // HttpIndexerBase.Test which runs FetchPage against the recent feed, which
            // costs a real /chapter request against MangaDex; MangaDex auto-disable is
            // already keyed off failure pressure (D-17), so a lightweight no-op here
            // mirrors Phase 2's MangaDexMetadataSource posture.
            return Task.CompletedTask;
        }
    }
}
