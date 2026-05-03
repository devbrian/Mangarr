using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Parser.Manga.Model;

namespace NzbDrone.Core.DecisionEngine.Manga
{
    // Sonarr divergence: NEW manga-side decision DTO per Phase 5 D-05 — see DIVERGENCE.md.
    // Mirrors DownloadDecision (TV) shape — carries RemoteChapter (manga) + Rejections list.
    // Phase 6 dispatchers consume this via IMakeMangaDownloadDecision.GetRssDecision / GetSearchDecision.
    public class MangaDownloadDecision
    {
        public RemoteChapter RemoteChapter { get; private set; }
        public IEnumerable<DownloadRejection> Rejections { get; private set; }

        public bool Approved => !Rejections.Any();

        public bool TemporarilyRejected =>
            Rejections.Any() && Rejections.All(r => r.Type == RejectionType.Temporary);

        public bool Rejected =>
            Rejections.Any() && Rejections.Any(r => r.Type == RejectionType.Permanent);

        public MangaDownloadDecision(RemoteChapter remoteChapter, params DownloadRejection[] rejections)
        {
            RemoteChapter = remoteChapter;
            Rejections = rejections.ToList();
        }

        public override string ToString()
        {
            if (Approved)
            {
                return "[OK] " + RemoteChapter;
            }

            return "[Rejected " + Rejections.Count() + "] " + RemoteChapter;
        }
    }
}
