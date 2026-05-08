using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Common.Http;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Indexers
{
    // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — TV-shape Fetch overloads
    // (SeasonSearchCriteria / Single|Daily|Anime|SpecialEpisodeSearchCriteria, etc.)
    // stripped per Plan 15-10 IndexerSearch/Definitions DELETE. Manga overloads now canonical.
    public interface IIndexer : IProvider
    {
        bool SupportsRss { get; }
        bool SupportsSearch { get; }
        DownloadProtocol Protocol { get; }

        Task<IList<ReleaseInfo>> FetchRecent();

        Task<IList<ReleaseInfo>> Fetch(MangaSearchCriteria searchCriteria);
        Task<IList<ReleaseInfo>> Fetch(ChapterSearchCriteria searchCriteria);

        HttpRequest GetDownloadRequest(string link);
    }
}
