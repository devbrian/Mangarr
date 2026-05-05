using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Download.Pending.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 9 D-09-06.
    // Role-match analog: src/NzbDrone.Core/Download/Pending/PendingReleaseRepository.cs (interface).
    //
    // Phase 14 cleanup: collapse with IPendingReleaseRepository when Tv/ deletes.
    public interface IMangaPendingReleaseRepository : IBasicRepository<MangaPendingRelease>
    {
        void DeleteByMangaIds(List<int> mangaIds);
        List<MangaPendingRelease> AllByMangaId(int mangaId);
        List<MangaPendingRelease> WithoutFallback();
    }
}
