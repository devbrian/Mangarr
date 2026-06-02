using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Download.History.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 36 (LOOP-02) — see DIVERGENCE.md.
    // Provenance: v5-develop:src/NzbDrone.Core/Download/History/DownloadHistoryRepository.cs
    //   (IDownloadHistoryRepository).
    // Role-match analog: src/NzbDrone.Core/History/Manga/IChapterHistoryRepository.cs.
    //
    // TWO-SURFACE NOTE: queries the lean MangaDownloadHistory matching join, NOT the user-facing
    // ChapterHistory. The DownloadId finders here back the LOOP-02 matcher's GetLatestGrab hot path.
    public interface IMangaDownloadHistoryRepository : IBasicRepository<MangaDownloadHistory>
    {
        List<MangaDownloadHistory> FindByDownloadId(string downloadId);
        MangaDownloadHistory GetLatestGrab(string downloadId);
    }
}
