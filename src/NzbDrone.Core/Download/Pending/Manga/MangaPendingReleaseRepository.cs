using System.Collections.Generic;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Download.Pending.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 9 D-09-06 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Download/Pending/PendingReleaseRepository.cs.
    //
    // BL-01 GUARD: a separate Mapper.Entity<MangaPendingRelease>("MangaPendingReleases")
    // registration in TableMapping.cs makes it physically impossible to leak rows from
    // the TV-side PendingReleases table — two physical tables, two registrations. Mirrors
    // the BL-01 mechanical guarantee documented at MangaBlocklistRepository.cs:11-15.
    //
    // Predicate shape note (Pitfall 4 ordering does not apply at the repo layer — it's a
    // SERVICE-layer concern, see Plan 09-10 MangaPendingReleaseService.Save wrapper):
    // accessors here are pure persistence shims; matching/filtering rides Dapper's typed
    // LINQ-shape predicates which compile to parameterized SQL.
    //
    // Phase 14 cleanup: collapse with PendingReleaseRepository when Tv/ deletes.
    public class MangaPendingReleaseRepository : BasicRepository<MangaPendingRelease>, IMangaPendingReleaseRepository
    {
        public MangaPendingReleaseRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public void DeleteByMangaIds(List<int> mangaIds)
        {
            Delete(r => mangaIds.Contains(r.MangaId));
        }

        public List<MangaPendingRelease> AllByMangaId(int mangaId)
        {
            return Query(p => p.MangaId == mangaId);
        }

        public List<MangaPendingRelease> WithoutFallback()
        {
            // Fully-qualify NzbDrone.Core.Manga.Manga to dodge the namespace shadow risk
            // (this file lives under NzbDrone.Core.Download.Pending.Manga; an unqualified
            // `Manga` would resolve to the local sub-namespace, not the POCO type).
            // Mirrors Plan 07-01 Rule-3 precedent logged in STATE.md.
            var builder = new SqlBuilder(_database.DatabaseType)
                .InnerJoin<MangaPendingRelease, NzbDrone.Core.Manga.Manga>((p, m) => p.MangaId == m.Id)
                .Where<MangaPendingRelease>(p => p.Reason != PendingReleaseReason.Fallback);

            return Query(builder);
        }
    }
}
