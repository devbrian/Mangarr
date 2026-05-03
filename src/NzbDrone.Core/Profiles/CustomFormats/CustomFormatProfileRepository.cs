using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Profiles.CustomFormats
{
    public interface ICustomFormatProfileRepository : IBasicRepository<CustomFormatProfile>
    {
        bool Exists(int id);
    }

    // Sonarr divergence: NEW repository per Phase 5 D-07 — see DIVERGENCE.md.
    // CRITICAL (Pitfall 3): NO method overrides. BasicRepository<T> CRUD verbatim — preserves
    // the Polly + busy_timeout retry envelope from Phase 1 D-15. Phase 8 cleanup: collapse to
    // canonical Profiles/ namespace when Tv/ deletes.
    public class CustomFormatProfileRepository : BasicRepository<CustomFormatProfile>, ICustomFormatProfileRepository
    {
        public CustomFormatProfileRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public bool Exists(int id) => Query(p => p.Id == id).Count == 1;
    }
}
