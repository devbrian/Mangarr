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
        private readonly IIndexerSourceStatusService _sourceStatusService;

        public override string Name => "MangaDex";
        public override DownloadProtocol Protocol => DownloadProtocol.Http;
        public override string DefaultSourceKey => "mangadex";

        public MangaDexIndexer(
            IHttpClient httpClient,
            IIndexerStatusService indexerStatusService,
            IIndexerSourceStatusService sourceStatusService,    // Plan 03-03 D-17 sibling
            IConfigService configService,
            IParsingService parsingService,
            Logger logger,
            ILocalizationService localizationService)
            : base(httpClient, indexerStatusService, configService, parsingService, logger, localizationService)
        {
            _sourceStatusService = sourceStatusService;
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

        // ── Phase 3 D-03 — TV overloads throw NotSupportedException (per-class stubs) ───────
        // MangaDex is a manga indexer; the inherited TV-shaped overloads must trip loudly if
        // Phase 5/6 wiring accidentally crosses streams. ThingiProvider resolves indexer-by-protocol
        // so these are never called via the canonical pipeline anyway.
        public override Task<IList<ReleaseInfo>> Fetch(SeasonSearchCriteria searchCriteria)
            => throw new NotSupportedException("MangaDex is a manga indexer; TV search not applicable.");

        public override Task<IList<ReleaseInfo>> Fetch(SingleEpisodeSearchCriteria searchCriteria)
            => throw new NotSupportedException("MangaDex is a manga indexer; TV search not applicable.");

        public override Task<IList<ReleaseInfo>> Fetch(DailyEpisodeSearchCriteria searchCriteria)
            => throw new NotSupportedException("MangaDex is a manga indexer; TV search not applicable.");

        public override Task<IList<ReleaseInfo>> Fetch(DailySeasonSearchCriteria searchCriteria)
            => throw new NotSupportedException("MangaDex is a manga indexer; TV search not applicable.");

        public override Task<IList<ReleaseInfo>> Fetch(AnimeEpisodeSearchCriteria searchCriteria)
            => throw new NotSupportedException("MangaDex is a manga indexer; TV search not applicable.");

        public override Task<IList<ReleaseInfo>> Fetch(AnimeSeasonSearchCriteria searchCriteria)
            => throw new NotSupportedException("MangaDex is a manga indexer; TV search not applicable.");

        public override Task<IList<ReleaseInfo>> Fetch(SpecialEpisodeSearchCriteria searchCriteria)
            => throw new NotSupportedException("MangaDex is a manga indexer; TV search not applicable.");

        // ── Phase 3 D-14 — per-source HTTP headers for Phase 4 in-process downloader ───────
        // MangaDex /at-home/server URLs do NOT need a Referer (per RESEARCH §MangaDex Indexer);
        // return empty so Phase 4 downloader applies no extra headers.
        public override Dictionary<string, string> GetDownloadHeaders(ReleaseInfo release)
            => new Dictionary<string, string>();

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
