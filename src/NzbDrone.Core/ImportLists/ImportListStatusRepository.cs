using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider.Status;

namespace NzbDrone.Core.ImportLists
{
    // Phase 26 Plan 26-04 (D-13 — repo #2 of 3) — verbatim port from
    // .planning/reference/sonarr-vertical-slices/import-lists/ImportListStatusRepository.cs.
    // Inherits FindByProviderId + DeleteByProviderId from ProviderStatusRepository<T>;
    // backs ImportListStatusService.GetProviderStatus / RecordSuccess / RecordFailure /
    // RecordConnectionFailure (ProviderStatusServiceBase escalation/backoff plumbing).
    public interface IImportListStatusRepository : IProviderStatusRepository<ImportListStatus>
    {
    }

    public class ImportListStatusRepository : ProviderStatusRepository<ImportListStatus>, IImportListStatusRepository
    {
        public ImportListStatusRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }
}
