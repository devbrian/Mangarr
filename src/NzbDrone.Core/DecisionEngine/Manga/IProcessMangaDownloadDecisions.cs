using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Download;

namespace NzbDrone.Core.DecisionEngine.Manga
{
    public interface IProcessMangaDownloadDecisions
    {
        Task<ProcessedMangaDecisions> ProcessDecisions(List<MangaDownloadDecision> decisions);
        Task<ProcessedDecisionResult> ProcessDecision(MangaDownloadDecision decision, int? downloadClientId);
    }
}
