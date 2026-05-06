using System.Collections.Generic;

namespace NzbDrone.Core.DecisionEngine.Manga
{
    public class ProcessedMangaDecisions
    {
        public List<MangaDownloadDecision> Grabbed { get; set; }
        public List<MangaDownloadDecision> Pending { get; set; }
        public List<MangaDownloadDecision> Rejected { get; set; }

        public ProcessedMangaDecisions(List<MangaDownloadDecision> grabbed,
                                       List<MangaDownloadDecision> pending,
                                       List<MangaDownloadDecision> rejected)
        {
            Grabbed = grabbed;
            Pending = pending;
            Rejected = rejected;
        }
    }
}
