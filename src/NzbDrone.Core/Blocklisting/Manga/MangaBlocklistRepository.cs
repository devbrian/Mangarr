using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Blocklisting.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-11 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Blocklisting/BlocklistRepository.cs.
    //
    // BL-01 GUARD: BlocklistedByTitle / BlocklistedByReleaseGuid query the MangaBlocklist
    // table only — separate Mapper.Entity registration in TableMapping.cs makes it physically
    // impossible to leak rows from the TV-side Blocklist table even when MangaId / SeriesId
    // ints collide. Mirrors the BL-01 mechanical guarantee documented in History/Manga/.
    //
    // Phase 8 cleanup: collapse with BlocklistRepository when Tv/ deletes.
    public interface IMangaBlocklistRepository : IBasicRepository<MangaBlocklist>
    {
        List<MangaBlocklist> BlocklistedByTitle(int mangaId, string sourceTitle);
        List<MangaBlocklist> BlocklistedByReleaseGuid(int mangaId, string releaseGuid);
        List<MangaBlocklist> BlocklistedByManga(int mangaId);
        void DeleteForManga(int mangaId);
    }

    public class MangaBlocklistRepository : BasicRepository<MangaBlocklist>, IMangaBlocklistRepository
    {
        public MangaBlocklistRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<MangaBlocklist> BlocklistedByTitle(int mangaId, string sourceTitle)
        {
            // Pitfall 5 mitigation: matching is delegated to MangaBlocklistService which performs
            // trim + lowercase + OrdinalIgnoreCase comparison + null-tolerant SourceKey fallback.
            // The repository's job here is to narrow to candidate rows only — the TV analog uses
            // SQLite's LIKE (Contains) for the same role at BlocklistRepository.cs:23-27. We use
            // Query-everything-by-MangaId then filter in-memory in the service to keep the
            // case-insensitive compare consistent across SQLite + Postgres (SQLite default
            // collation is case-sensitive on user columns; Postgres default is case-sensitive
            // unless the column is CITEXT). Manga blocklists are small per-manga (typical: <10
            // rows) so the in-memory pass is cheap.
            return Query(b => b.MangaId == mangaId).ToList();
        }

        public List<MangaBlocklist> BlocklistedByReleaseGuid(int mangaId, string releaseGuid)
        {
            return Query(b => b.MangaId == mangaId && b.ReleaseGuid == releaseGuid).ToList();
        }

        public List<MangaBlocklist> BlocklistedByManga(int mangaId)
        {
            return Query(b => b.MangaId == mangaId).ToList();
        }

        public void DeleteForManga(int mangaId)
        {
            Delete(b => b.MangaId == mangaId);
        }
    }
}
