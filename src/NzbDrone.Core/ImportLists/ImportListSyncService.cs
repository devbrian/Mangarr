using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.ImportLists.ImportListItems;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource;
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
        private readonly IMetadataSourceFactory _metadataSourceFactory;
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
                              IMetadataSourceFactory metadataSourceFactory,
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
            _metadataSourceFactory = metadataSourceFactory;
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

        private void SyncAll(bool ignoreRefreshInterval = false)
        {
            if (_importListFactory.AutomaticAddEnabled().Empty())
            {
                _logger.Debug("No import lists with automatic add enabled");

                return;
            }

            _logger.ProgressInfo("Starting Import List Sync");

            var result = _listFetcherAndParser.Fetch(ignoreRefreshInterval);

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

            // GH #241 follow-up: also load existing AniListId/MalId sets so we can
            // short-circuit cross-source resolution when the item already corresponds
            // to a library manga via its alternate ID. Without this, every AniList/MAL-
            // only item (they NEVER carry MangaDexId from upstream) would hit
            // MangaDex search on every 24h sync, burning the shared "mangadex" 40 req/min
            // budget even though the dedup at the bottom of the loop would reject the
            // resolved row anyway. HashSet for O(1) lookup; per-sync snapshot is fine
            // because MangaAddedEvent fires after the loop completes.
            var existingAniListIds = new HashSet<int>(_mangaService.AllAniListIds() ?? Enumerable.Empty<int>());
            var existingMalIds = new HashSet<int>(_mangaService.AllMalIds() ?? Enumerable.Empty<int>());

            // GH #241 / Codex review: when MangaDex returns 429 on a cross-source
            // search, further per-item searches will also throttle. Latch this flag
            // and skip subsequent cross-source lookups (items with only AniListId/MalId)
            // for the remainder of this sync — the next scheduled sync (24h cadence)
            // will retry once the budget resets. Items that already carry MangaDexId
            // are unaffected.
            var crossSourceThrottled = false;

            foreach (var item in items)
            {
                _logger.ProgressTrace("Processing list item {0}/{1}", reportNumber, items.Count);

                reportNumber++;

                var importList = importLists.Single(x => x.Id == item.ImportListId);

                if (!importList.EnableAutomaticAdd)
                {
                    continue;
                }

                // GH #241 v1.2 cross-source resolution wire-in:
                // when an item arrives from AniList/MAL with only AniListId/MalId set,
                // ask the primary metadata source (MangaDex by default) to search by title
                // and pick the candidate whose own links.al / links.mal matches the item's
                // cross-source id. This is the minimum-viable resolver — full Jaro-Winkler +
                // multi-axis confirm (CrossSourceIdResolver) is a v1.2+ tightening for the
                // ambiguous-title case.
                if (item.MangaDexId.IsNullOrWhiteSpace())
                {
                    // GH #241 follow-up: short-circuit when this item is already in the
                    // library by AniListId or MalId match. Without this guard, every
                    // AniList/MAL-only item burns one MangaDex search per 24h sync (the
                    // upstream NEVER carries MangaDexId, so the cross-source lookup runs
                    // every time and the dedup at the bottom rejects the result). With
                    // the guard, in-library items skip the lookup entirely.
                    if ((item.AniListId.HasValue && existingAniListIds.Contains(item.AniListId.Value)) ||
                        (item.MalId.HasValue && existingMalIds.Contains(item.MalId.Value)))
                    {
                        _logger.Debug(
                            "[{0}] Rejected, already in library by alternate-ID (AniList={1}/MAL={2}); skipping cross-source lookup",
                            item.Title,
                            item.AniListId,
                            item.MalId);
                        continue;
                    }

                    if ((item.AniListId.HasValue || item.MalId.HasValue) && !crossSourceThrottled)
                    {
                        try
                        {
                            // GetPrimary throws InvalidOperationException when no primary metadata
                            // source is configured (config-drift). The catch on that exception
                            // below silently falls through to the standard "no MangaDexId" reject
                            // path — cross-source resolution is opportunistic, not load-bearing.
                            var primaryDef = _metadataSourceFactory.GetPrimary();
                            var primary = primaryDef != null
                                ? _metadataSourceFactory.GetInstance(primaryDef)
                                : null;

                            if (primary != null)
                            {
                                var candidates = primary.SearchForNewManga(item.Title) ?? new List<Manga.Manga>();

                                // CodeRabbit review: when an item carries BOTH AniListId AND MalId,
                                // require BOTH to agree with the candidate. The OR-on-either pre-fix
                                // could pick a wrong candidate that happened to share only one ID
                                // (e.g. title-collision sibling that has same MalId but different
                                // AniListId). When an item carries only one of the IDs, the missing
                                // clause is short-circuited (no opinion). The outer if guarantees at
                                // least one ID is present so this never degenerates to "match any".
                                var match = candidates.FirstOrDefault(c =>
                                    (!item.AniListId.HasValue || c.AniListId == item.AniListId) &&
                                    (!item.MalId.HasValue || c.MalId == item.MalId));

                                if (match?.MangaDexId != null)
                                {
                                    item.MangaDexId = match.MangaDexId.ToString();
                                    _logger.Debug(
                                        "[{0}] Cross-source resolved AniList={1}/MAL={2} → MangaDexId={3}",
                                        item.Title,
                                        item.AniListId,
                                        item.MalId,
                                        item.MangaDexId);
                                }
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            // No primary metadata source configured (or factory has no providers
                            // wired). Fall through silently — cross-source resolution is best-effort.
                        }
                        catch (TooManyRequestsException)
                        {
                            // Codex review: MangaDex returned 429 (server-side budget exceeded —
                            // the local RateLimit blocking-wait did NOT prevent it because the
                            // "mangadex" SourceKey is SHARED with MetadataSource/Indexer/image-
                            // downloader and we may have drained the budget through those peers).
                            // Latch the flag so we don't keep hammering for the remainder of this
                            // sync run; surface a single visible warning. The next scheduled sync
                            // (24h cadence) will retry once the budget resets.
                            crossSourceThrottled = true;
                            _logger.Warn(
                                "Cross-source ID lookup against MangaDex throttled (HTTP 429); skipping cross-source resolution for the rest of this sync. Item [{0}] and subsequent AniList/MAL-only items will be retried on the next scheduled sync.",
                                item.Title);
                        }
                        catch (Exception ex)
                        {
                            _logger.Warn(ex, "[{0}] Cross-source resolution failed; item will be skipped", item.Title);
                        }
                    }

                    if (item.MangaDexId.IsNullOrWhiteSpace())
                    {
                        _logger.Debug("[{0}] Rejected, no MangaDexId — cross-source resolution did not find a match", item.Title);
                        continue;
                    }
                }

                // CodeRabbit PR #218 (initial review + outside-diff follow-up):
                // validate MangaDexId is a real GUID AND canonicalize it before every
                // dedup comparison. Guid.TryParse accepts multiple valid formats
                // (with/without braces, various hyphenation patterns) but Guid.ToString()
                // produces a single canonical 8-4-4-4-12 form. Comparing the original
                // string against existingMangaDexIds / mangaToAdd / exclusions would
                // false-negative on non-canonical inputs (e.g., uppercase, braces),
                // letting the same manga slip through dedup and accumulate duplicates.
                // Parse once, then use the canonical string for ALL three checks below.
                if (!Guid.TryParse(item.MangaDexId, out var mangaDexGuid))
                {
                    _logger.Debug("[{0}] Rejected, MangaDexId '{1}' is not a valid GUID", item.Title, item.MangaDexId);
                    continue;
                }

                var canonicalMangaDexId = mangaDexGuid.ToString();

                // Check to see if manga excluded — use canonical form for the
                // string-equality comparison since stored exclusions are also
                // canonical (Manga.MangaDexId?.ToString() on the delete-event path).
                var excludedManga = listExclusions.SingleOrDefault(s => s.MangaDexId == canonicalMangaDexId);

                if (excludedManga != null)
                {
                    _logger.Debug("{0} [{1}] Rejected due to list exclusion", canonicalMangaDexId, item.Title);
                    continue;
                }

                // Break if Manga Exists in DB — existingMangaDexIds is also canonical
                // (Manga.MangaDexId is Guid?; .Select(g => g.ToString()) emits canonical form).
                if (existingMangaDexIds.Any(x => x == canonicalMangaDexId))
                {
                    _logger.Debug("{0} [{1}] Rejected, manga exists in database", canonicalMangaDexId, item.Title);
                    continue;
                }

                // Append Manga if not already in DB or already on add list — compare
                // Guid? values directly to avoid any remaining canonicalization mismatch.
                if (mangaToAdd.All(m => m.MangaDexId != mangaDexGuid))
                {
                    var monitored = importList.ShouldMonitor != MonitorTypes.None;

                    mangaToAdd.Add(new Manga.Manga
                    {
                        MangaDexId = mangaDexGuid,
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

                            // CodeRabbit PR #218: preserve Existing/First semantics
                            // instead of silently coercing to All. Sonarr's
                            // MonitorTypes.Existing = "monitor existing chapters,
                            // no backfill"; MonitorTypes.First = "monitor first
                            // season only" (a TV concept). For manga's flat chapter
                            // list, both map most closely to Latest (monitor recent
                            // chapters, do not backfill the entire library).
                            // Unknown values fall through to None as a fail-safe so
                            // a future enum addition does not silently start
                            // monitoring everything.
                            Monitor = importList.ShouldMonitor switch
                            {
                                MonitorTypes.All => MangaMonitor.All,
                                MonitorTypes.Latest => MangaMonitor.Latest,
                                MonitorTypes.Existing => MangaMonitor.Latest,
                                MonitorTypes.First => MangaMonitor.Latest,
                                MonitorTypes.None => MangaMonitor.None,
                                _ => MangaMonitor.None
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
                // GH #241 follow-up: user-initiated "Sync All" should run all lists
                // immediately, even if the scheduled 24h cadence hasn't elapsed since
                // the last run. The MinRefreshInterval gate is a courtesy to upstream
                // APIs on the SCHEDULED cadence, not a hard throttle — when the user
                // explicitly clicks "Sync All" (CommandTrigger.Manual), bypass it.
                // CommandTrigger.Unspecified is treated as Scheduled for safety
                // (programmatic callers that don't set Trigger should not bypass).
                SyncAll(ignoreRefreshInterval: message.Trigger == CommandTrigger.Manual);
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
