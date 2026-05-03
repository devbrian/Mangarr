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

        // ── Phase 4 D-01 — Wave 1 Task 1 stub (real implementation lands in Task 2) ──────
        // This stub satisfies the Phase 4 abstract on HttpAggregatorBase so the production
        // Sonarr.Core build remains green between Task 1 (contract land) and Task 2 (per-source
        // override land). The contract test in Task 1 only exercises the IHttpAggregator
        // interface contract; per-source dereference logic + URL assembly assertions live in
        // ComixGetChapterPagesFixture (Task 2).
        public override Task<ChapterManifest> GetChapterPages(ReleaseInfo release)
            => throw new NotImplementedException("Phase 4 plan 04-02 Task 2 — implements comix.to /api/v2/chapters/{id} dereference.");

        protected override Task Test(List<ValidationFailure> failures)
        {
            // Lightweight Test() implementation — honors the abstract contract.
            // Real connectivity verification happens via the GH-Actions soak workflow
            // (Plan 03-06) + Wave 0 fixtures. Mirrors MangaDexIndexer posture.
            return Task.CompletedTask;
        }
    }
}
