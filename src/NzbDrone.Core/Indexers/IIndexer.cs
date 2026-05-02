using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Common.Http;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Indexers
{
    public interface IIndexer : IProvider
    {
        bool SupportsRss { get; }
        bool SupportsSearch { get; }
        DownloadProtocol Protocol { get; }

        Task<IList<ReleaseInfo>> FetchRecent();

        // ── Existing TV-shaped overloads (UNCHANGED; Phase 8 deletes with Tv/) ─────
        Task<IList<ReleaseInfo>> Fetch(SeasonSearchCriteria searchCriteria);
        Task<IList<ReleaseInfo>> Fetch(SingleEpisodeSearchCriteria searchCriteria);
        Task<IList<ReleaseInfo>> Fetch(DailyEpisodeSearchCriteria searchCriteria);
        Task<IList<ReleaseInfo>> Fetch(DailySeasonSearchCriteria searchCriteria);
        Task<IList<ReleaseInfo>> Fetch(AnimeEpisodeSearchCriteria searchCriteria);
        Task<IList<ReleaseInfo>> Fetch(AnimeSeasonSearchCriteria searchCriteria);
        Task<IList<ReleaseInfo>> Fetch(SpecialEpisodeSearchCriteria searchCriteria);

        // ── NEW manga-shaped overloads (Phase 3 D-01; additive only) ──────────────
        // TV indexers (Newznab, Nyaa, Torznab, etc.) inherit virtual default-empty impls
        // from IndexerBase so they compile clean without behavior change. HttpAggregatorBase
        // descendants (Phase 3 manga plugins) MUST implement these concretely (D-02).
        Task<IList<ReleaseInfo>> Fetch(MangaSearchCriteria searchCriteria);
        Task<IList<ReleaseInfo>> Fetch(ChapterSearchCriteria searchCriteria);

        HttpRequest GetDownloadRequest(string link);
    }
}
