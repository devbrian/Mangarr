using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Profiles.Translations
{
    public interface ITranslationProfileRepository : IBasicRepository<TranslationProfile>
    {
        bool Exists(int id);
    }

    // Sonarr divergence: NEW repository per Phase 5 D-01 — see DIVERGENCE.md.
    // CRITICAL (Pitfall 3): NO method overrides. BasicRepository<T> CRUD verbatim — preserves
    // the Polly + busy_timeout retry envelope from Phase 1 D-15. Adding an Insert/Update/Delete
    // override without calling base.X() bypasses Polly retry. Phase 8 cleanup: collapse to canonical
    // Profiles/ namespace when Tv/ deletes.
    public class TranslationProfileRepository : BasicRepository<TranslationProfile>, ITranslationProfileRepository
    {
        public TranslationProfileRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public bool Exists(int id)
        {
            return Query(p => p.Id == id).Count == 1;
        }
    }
}
