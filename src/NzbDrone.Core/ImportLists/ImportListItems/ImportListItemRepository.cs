using System.Collections.Generic;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.ImportLists.ImportListItems
{
    // Phase 26 Plan 26-04 — verbatim port from
    // .planning/reference/sonarr-vertical-slices/import-lists/ImportListItems/ImportListItemRepository.cs.
    public interface IImportListItemRepository : IBasicRepository<ImportListItemInfo>
    {
        List<ImportListItemInfo> GetAllForLists(List<int> listIds);
    }

    public class ImportListItemRepository : BasicRepository<ImportListItemInfo>, IImportListItemRepository
    {
        public ImportListItemRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<ImportListItemInfo> GetAllForLists(List<int> listIds)
        {
            return Query(x => listIds.Contains(x.ImportListId));
        }
    }
}
