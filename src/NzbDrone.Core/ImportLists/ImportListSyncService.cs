using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.ImportLists.ImportListItems;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.ImportLists
{
    // Phase 26 Plan 26-04 (IL-03) — ported from
    // .planning/reference/sonarr-vertical-slices/import-lists/ImportListSyncService.cs.
    //
    // Manga-shape swaps (RESEARCH §Q1 + §Pattern):
    //   * AddSeriesService → IAddMangaService.AddManga(List<Manga>, true) bulk overload
    //     (Phase 8 audit gap-01 shipped — publishes MangaImportedEvent which the
    //     Phase 24 AutoTagging applier rides via IHandle<MangaAddedEvent>).
    //   * SeriesSearchService cross-source ID resolution (TVDB/IMDB/TMDB/AniList/MAL →
    //     TvdbId)  → DROPPED. Phase 27 owns AniList/MAL → MangaDexId resolution; until
    //     then ImportListSyncService only auto-adds items that already carry a MangaDexId.
    //   * existingTvdbIds → AllMangaDexIds() — Phase 8 audit gap-11 cross-source ID lists.
    //   * exclusion check: TvdbId equality → MangaDexId string equality.
    //
    // Per RESEARCH §Q5 + Open Q #3, ImportListSyncService deliberately leaves the
    // monitor + profile fan-out skinny: ShouldMonitor / MonitorNewItems / TranslationProfile /
    // CustomFormatProfile / RootFolderPath flow through from the ImportListDefinition;
    // future per-list `SearchForMissingChapters` cascade lives behind
    // ImportListDefinition.SearchForMissingChapters.
    public class ImportListSyncService : IExecute<ImportListSyncCommand>, IHandleAsync<ProviderDeletedEvent<IMangaImportList>>
    {
        private readonly IImportListFactory _importListFactory;
        private readonly IImportListStatusService _importListStatusService;
        private readonly IImportListExclusionService _importListExclusionService;
        private readonly IImportListItemService _importListItemService;
        private readonly IFetchAndParseImportList _listFetcherAndParser;
        private readonly IMangaService _mangaService;
        private readonly IAddMangaService _addMangaService;
        private readonly IConfigService _configService;
        private readonly ITaskManager _taskManager;
        private readonly Logger _logger;

        public ImportListSyncService(IImportListFactory importListFactory,
                              IImportListStatusService importListStatusService,
                              IImportListExclusionService importListExclusionService,
                              IImportListItemService importListItemService,
                              IFetchAndParseImportList listFetcherAndParser,
                              IMangaService mangaService,
                              IAddMangaService addMangaService,
                              IConfigService configService,
                              ITaskManager taskManager,
                              Logger logger)
        {
            _importListFactory = importListFactory;
            _importListStatusService = importListStatusService;
            _importListExclusionService = importListExclusionService;
            _importListItemService = importListItemService;
            _listFetcherAndParser = listFetcherAndParser;
            _mangaService = mangaService;
            _addMangaService = addMangaService;
            _configService = configService;
            _taskManager = taskManager;
            _logger = logger;
        }

        private bool AllListsSuccessfulWithAPendingClean()
        {
            var lists = _importListFactory.AutomaticAddEnabled(false);
            var anyRemoved = false;

            foreach (var list in lists)
            {
                var status = _importListStatusService.GetListStatus(list.Definition.Id);

                if (status.DisabledTill.HasValue)
                {
                    // list failed the last time it was synced.
                    return false;
                }

                if (!status.LastInfoSync.HasValue)
                {
                    // list has never been synced.
                    return false;
                }

                anyRemoved |= status.HasRemovedItemSinceLastClean;
            }

            return anyRemoved;
        }

        private void SyncAll()
        {
            if (_importListFactory.AutomaticAddEnabled().Empty())
            {
                _logger.Debug("No import lists with automatic add enabled");

                return;
            }

            _logger.ProgressInfo("Starting Import List Sync");

            var result = _listFetcherAndParser.Fetch();

            var listItems = result.Manga.ToList();

            ProcessListItems(listItems);

            TryCleanLibrary();
        }

        private void SyncList(ImportListDefinition definition)
        {
            _logger.ProgressInfo("Starting Import List Refresh for List {0}", definition.Name);

            var result = _listFetcherAndParser.FetchSingleList(definition);

            var listItems = result.Manga.ToList();

            ProcessListItems(listItems);

            TryCleanLibrary();
        }

        private void ProcessListItems(List<ImportListItemInfo> items)
        {
            var mangaToAdd = new List<Manga.Manga>();

            if (items.Count == 0)
            {
                _logger.ProgressInfo("No list items to process");

                return;
            }

            _logger.ProgressInfo("Processing {0} list items", items.Count);

            var reportNumber = 1;

            var listExclusions = _importListExclusionService.All();
            var importLists = _importListFactory.All();
            var existingMangaDexIds = _mangaService.AllMangaDexIds()
                                                   .Select(g => g.ToString())
                                                   .ToList();

            foreach (var item in items)
            {
                _logger.ProgressTrace("Processing list item {0}/{1}", reportNumber, items.Count);

                reportNumber++;

                var importList = importLists.Single(x => x.Id == item.ImportListId);

                if (!importList.EnableAutomaticAdd)
                {
                    continue;
                }

                // Phase 27 owns cross-source ID resolution (AniList/MAL → MangaDexId).
                // Until then, items without a MangaDexId are skipped — the substrate is
                // ready for the lookup to be wired in; the lookup itself is the provider
                // work that Phase 27 plans alongside the concrete providers.
                if (item.MangaDexId.IsNullOrWhiteSpace())
                {
                    _logger.Debug("[{0}] Rejected, no MangaDexId — AniList/MAL cross-source resolution lives in Phase 27", item.Title);
                    continue;
                }

                // Check to see if manga excluded
                var excludedManga = listExclusions.SingleOrDefault(s => s.MangaDexId == item.MangaDexId);

                if (excludedManga != null)
                {
                    _logger.Debug("{0} [{1}] Rejected due to list exclusion", item.MangaDexId, item.Title);
                    continue;
                }

                // Break if Manga Exists in DB
                if (existingMangaDexIds.Any(x => x == item.MangaDexId))
                {
                    _logger.Debug("{0} [{1}] Rejected, manga exists in database", item.MangaDexId, item.Title);
                    continue;
                }

                // Append Manga if not already in DB or already on add list
                if (mangaToAdd.All(m => m.MangaDexId?.ToString() != item.MangaDexId))
                {
                    var monitored = importList.ShouldMonitor != MonitorTypes.None;

                    mangaToAdd.Add(new Manga.Manga
                    {
                        MangaDexId = Guid.TryParse(item.MangaDexId, out var dexGuid) ? dexGuid : (Guid?)null,
                        MalId = item.MalId,
                        AniListId = item.AniListId,
                        Title = item.Title,
                        Monitored = monitored,
                        MonitorNewItems = importList.MonitorNewItems == NewItemMonitorTypes.All
                                          ? MangaMonitorNewItems.All
                                          : MangaMonitorNewItems.None,
                        RootFolderPath = importList.RootFolderPath,
                        TranslationProfileId = importList.TranslationProfileId,
                        CustomFormatProfileId = importList.CustomFormatProfileId,
                        Tags = importList.Tags,
                        AddOptions = new AddMangaOptions
                        {
                            SearchForMissingChapters = importList.SearchForMissingChapters,
                            Monitor = importList.ShouldMonitor switch
                            {
                                MonitorTypes.All => MangaMonitor.All,
                                MonitorTypes.Latest => MangaMonitor.Latest,
                                MonitorTypes.None => MangaMonitor.None,
                                _ => MangaMonitor.All
                            }
                        }
                    });
                }
            }

            _addMangaService.AddManga(mangaToAdd, true);

            _logger.ProgressInfo("Import List Sync Completed. Items found: {0}, Manga added: {1}", items.Count, mangaToAdd.Count);
        }

        public void Execute(ImportListSyncCommand message)
        {
            if (message.DefinitionId.HasValue)
            {
                SyncList(_importListFactory.Get(message.DefinitionId.Value));
            }
            else
            {
                SyncAll();
            }
        }

        private void TryCleanLibrary()
        {
            if (_configService.ListSyncLevel == ListSyncLevelType.Disabled)
            {
                return;
            }

            if (AllListsSuccessfulWithAPendingClean())
            {
                CleanLibrary();
            }
        }

        private void CleanLibrary()
        {
            if (_configService.ListSyncLevel == ListSyncLevelType.Disabled)
            {
                return;
            }

            var mangaToUpdate = new List<Manga.Manga>();
            var mangaInLibrary = _mangaService.GetAllManga();
            var allListItems = _importListItemService.All();

            foreach (var manga in mangaInLibrary)
            {
                var mangaDexIdString = manga.MangaDexId?.ToString();

                var mangaExists = allListItems.Where(l =>
                    (mangaDexIdString != null && l.MangaDexId == mangaDexIdString) ||
                    ((manga.MalId ?? 0) > 0 && l.MalId == manga.MalId) ||
                    ((manga.AniListId ?? 0) > 0 && l.AniListId == manga.AniListId)).ToList();

                if (!mangaExists.Any())
                {
                    switch (_configService.ListSyncLevel)
                    {
                        case ListSyncLevelType.LogOnly:
                            _logger.Info("{0} was in your library, but not found in your lists --> You might want to unmonitor or remove it", manga);
                            break;
                        case ListSyncLevelType.KeepAndUnmonitor when manga.Monitored:
                            _logger.Info("{0} was in your library, but not found in your lists --> Keeping in library but unmonitoring it", manga);
                            manga.Monitored = false;
                            mangaToUpdate.Add(manga);
                            break;
                        case ListSyncLevelType.KeepAndTag when !manga.Tags.Contains(_configService.ListSyncTag):
                            _logger.Info("{0} was in your library, but not found in your lists --> Keeping in library but tagging it", manga);
                            manga.Tags.Add(_configService.ListSyncTag);
                            mangaToUpdate.Add(manga);
                            break;
                        default:
                            break;
                    }
                }
            }

            if (mangaToUpdate.Any())
            {
                _mangaService.UpdateManga(mangaToUpdate, true);
            }

            _importListStatusService.MarkListsAsCleaned();
        }

        public void HandleAsync(ProviderDeletedEvent<IMangaImportList> message)
        {
            TryCleanLibrary();
        }
    }
}
