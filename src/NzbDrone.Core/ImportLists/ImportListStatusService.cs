using System;
using System.Collections.Generic;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider.Status;

namespace NzbDrone.Core.ImportLists
{
    // Phase 26 Plan 26-04 — ported from
    // .planning/reference/sonarr-vertical-slices/import-lists/ImportListStatusService.cs
    // verbatim. Inherits ProviderStatusServiceBase's escalation/backoff machinery (and
    // IHandleAsync<ProviderDeletedEvent<IMangaImportList>> for cascade delete cleanup);
    // adds the ImportList-specific surface (GetListStatus, UpdateListSyncStatus,
    // MarkListsAsCleaned) consumed by FetchAndParseImportListService + ImportListSyncService.
    public interface IImportListStatusService : IProviderStatusServiceBase<ImportListStatus>
    {
        ImportListStatus GetListStatus(int importListId);

        void UpdateListSyncStatus(int importListId, bool removedItems);
        void MarkListsAsCleaned();
    }

    public class ImportListStatusService : ProviderStatusServiceBase<IMangaImportList, ImportListStatus>, IImportListStatusService
    {
        public ImportListStatusService(IImportListStatusRepository providerStatusRepository, IEventAggregator eventAggregator, IRuntimeInfo runtimeInfo, Logger logger)
            : base(providerStatusRepository, eventAggregator, runtimeInfo, logger)
        {
        }

        public ImportListStatus GetListStatus(int importListId)
        {
            return GetProviderStatus(importListId);
        }

        public void UpdateListSyncStatus(int importListId, bool removedItems)
        {
            lock (_syncRoot)
            {
                var status = GetProviderStatus(importListId);

                status.LastInfoSync = DateTime.UtcNow;
                status.HasRemovedItemSinceLastClean |= removedItems;

                _providerStatusRepository.Upsert(status);
            }
        }

        public void MarkListsAsCleaned()
        {
            lock (_syncRoot)
            {
                var toUpdate = new List<ImportListStatus>();

                foreach (var status in _providerStatusRepository.All())
                {
                    status.HasRemovedItemSinceLastClean = false;
                    toUpdate.Add(status);
                }

                _providerStatusRepository.UpdateMany(toUpdate);
            }
        }
    }
}
