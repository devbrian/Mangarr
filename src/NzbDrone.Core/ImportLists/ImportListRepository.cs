using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.ImportLists
{
    // Phase 26 Plan 26-04 (D-13 — repo #1 of 3) — ported from
    // .planning/reference/sonarr-vertical-slices/import-lists/ImportListRepository.cs
    // verbatim. `UpdateSettings(model)` writes only the Settings JSON column via
    // BasicRepository.SetFields — the V5 controller uses this to flip provider config
    // without re-publishing ProviderUpdatedEvent's full-edit cascade.
    //
    // Inherits CR-02 SQLITE_BUSY retry wrap from ProviderRepository<T>:46-83 — every
    // Find / Get / All / GetPaged read path is funneled through RetryStrategy automatically.
    public interface IImportListRepository : IProviderRepository<ImportListDefinition>
    {
        void UpdateSettings(ImportListDefinition model);
        ImportListDefinition FindByName(string name);
    }

    public class ImportListRepository : ProviderRepository<ImportListDefinition>, IImportListRepository
    {
        public ImportListRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public void UpdateSettings(ImportListDefinition model)
        {
            SetFields(model, m => m.Settings);
        }

        public ImportListDefinition FindByName(string name)
        {
            return Query(i => i.Name == name).SingleOrDefault();
        }
    }
}
