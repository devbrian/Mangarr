using System.Collections.Generic;
using System.Threading.Tasks;

namespace NzbDrone.Core.DecisionEngine.Manga
{
    public interface IProcessMangaDownloadDecisions
    {
        Task<ProcessedMangaDecisions> ProcessDecisions(List<MangaDownloadDecision> decisions);
    }
}
