using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.ImportLists.ImportListItems
{
    // Phase 26 Plan 26-04 — ported from
    // .planning/reference/sonarr-vertical-slices/import-lists/ImportListItems/ImportListItemService.cs
    // with manga-ID triplet swap in FindItem (TVDB/IMDB/TMDB → MangaDexId/MalId/AniListId).
    // SyncSeriesForList rename → SyncMangaForList per Mangarr peer naming.
    public interface IImportListItemService
    {
        List<ImportListItemInfo> All();
        List<ImportListItemInfo> GetAllForLists(List<int> listIds);
        int SyncMangaForList(List<ImportListItemInfo> listManga, int listId);
    }

    public class ImportListItemService : IImportListItemService, IHandleAsync<ProviderDeletedEvent<IMangaImportList>>
    {
        private readonly IImportListItemRepository _importListItemRepository;

        public ImportListItemService(IImportListItemRepository importListItemRepository)
        {
            _importListItemRepository = importListItemRepository;
        }

        public int SyncMangaForList(List<ImportListItemInfo> listManga, int listId)
        {
            var existingListManga = GetAllForLists(new List<int> { listId });

            var toAdd = new List<ImportListItemInfo>();
            var toUpdate = new List<ImportListItemInfo>();

            listManga.ForEach(item =>
            {
                var existingItem = FindItem(existingListManga, item);

                if (existingItem == null)
                {
                    toAdd.Add(item);
                    return;
                }

                // Remove so we'll only be left with items to remove at the end
                existingListManga.Remove(existingItem);
                toUpdate.Add(existingItem);

                existingItem.Title = item.Title;
                existingItem.MangaDexId = item.MangaDexId;
                existingItem.MalId = item.MalId;
                existingItem.AniListId = item.AniListId;
                existingItem.ReleaseDate = item.ReleaseDate;
            });

            _importListItemRepository.InsertMany(toAdd);
            _importListItemRepository.UpdateMany(toUpdate);
            _importListItemRepository.DeleteMany(existingListManga);

            return existingListManga.Count;
        }

        public List<ImportListItemInfo> All()
        {
            return _importListItemRepository.All().ToList();
        }

        public List<ImportListItemInfo> GetAllForLists(List<int> listIds)
        {
            return _importListItemRepository.GetAllForLists(listIds).ToList();
        }

        public void HandleAsync(ProviderDeletedEvent<IMangaImportList> message)
        {
            var mangaOnList = _importListItemRepository.GetAllForLists(new List<int> { message.ProviderId });
            _importListItemRepository.DeleteMany(mangaOnList);
        }

        private ImportListItemInfo FindItem(List<ImportListItemInfo> existingItems, ImportListItemInfo item)
        {
            return existingItems.FirstOrDefault(e =>
            {
                if (e.MangaDexId.IsNotNullOrWhiteSpace() && item.MangaDexId.IsNotNullOrWhiteSpace() && e.MangaDexId == item.MangaDexId)
                {
                    return true;
                }

                if ((e.MalId ?? 0) > 0 && (item.MalId ?? 0) > 0 && e.MalId == item.MalId)
                {
                    return true;
                }

                if ((e.AniListId ?? 0) > 0 && (item.AniListId ?? 0) > 0 && e.AniListId == item.AniListId)
                {
                    return true;
                }

                return false;
            });
        }
    }
}
