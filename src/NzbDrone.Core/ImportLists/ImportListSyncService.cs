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
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.MetadataSource.MangaBaka;
using NzbDrone.Core.MetadataSource.MangaDex;
using NzbDrone.Core.MetadataSource.MyAnimeList;
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
    // monitor + profile fan-out skinny: ShouldMonitor (the canonical 7-value MangaMonitor) /
    // TranslationProfile / CustomFormatProfile / RootFolderPath flow through from the
    // ImportListDefinition. MonitorNewItems is no longer a user-facing axis (#356) — it is
    // derived from ShouldMonitor via MangaMonitorExtensions.DeriveMonitorNewItems(). Future
    // per-list `SearchForMissingChapters` cascade lives behind
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

            // v1.3 (quick-260608-vf9 follow-up): resolve the active primary metadata source
            // ONCE per sync. Pre-v1.3 this whole method was hardcoded to MangaDexId, but
            // MangaBaka became the default primary (Phase 41) and
            // AddMangaService.PrepareForAdd -> ResolveSourceIdForPrimary REQUIRES the active
            // primary's OWN id (e.g. MangaBakaId) or it throws "no source ID for active
            // primary". So when the primary is NOT MangaDex, each item must be resolved to the
            // primary's id (StageViaPrimaryResolution) rather than to a MangaDexId — otherwise
            // every MAL/AniList-only item is silently rejected at the MangaDexId gate below.
            // When the primary IS MangaDex (or is unconfigured -> Unknown -> legacy default)
            // the original MangaDexId-centric block runs unchanged.
            MetadataSourceDefinition activePrimaryDef = null;
            try
            {
                activePrimaryDef = _metadataSourceFactory.GetPrimary();
            }
            catch (InvalidOperationException)
            {
                // No primary metadata source configured (config-drift) — fall back to the
                // legacy MangaDexId path so MangaDex-id-carrying items still add.
            }

            // GetInstance is deliberately OUTSIDE the catch above: if a primary IS configured
            // but its provider can't be instantiated (broken settings, missing impl), let that
            // surface as a failed sync instead of swallowing it — otherwise primaryKind would be
            // non-MangaDex while primarySource is null, routing every item into
            // StageViaPrimaryResolution where it is silently dropped (CodeRabbit #3377823322).
            var primarySource = activePrimaryDef != null
                ? _metadataSourceFactory.GetInstance(activePrimaryDef)
                : null;

            var primaryKind = ClassifyPrimary(activePrimaryDef);

            foreach (var item in items)
            {
                _logger.ProgressTrace("Processing list item {0}/{1}", reportNumber, items.Count);

                reportNumber++;

                var importList = importLists.Single(x => x.Id == item.ImportListId);

                if (!importList.EnableAutomaticAdd)
                {
                    continue;
                }

                // Non-MangaDex primary (e.g. MangaBaka, the v1.3 default): resolve the item to
                // the primary source's own id and stage it, then move on — the MangaDexId-centric
                // block below does not apply because the library/add path is keyed on the
                // primary's id, not MangaDexId.
                if (primaryKind != PrimaryKind.MangaDex && primaryKind != PrimaryKind.Unknown)
                {
                    StageViaPrimaryResolution(
                        item,
                        importList,
                        primarySource,
                        primaryKind,
                        mangaToAdd,
                        listExclusions,
                        existingMalIds,
                        existingAniListIds,
                        existingMangaDexIds,
                        ref crossSourceThrottled);

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
                        // CodeRabbit review (narrowed-IOE-fallback): GetPrimary throws
                        // InvalidOperationException when no primary metadata source is configured
                        // (config-drift). Keep that catch SCOPED to the primary-source-acquisition
                        // block ONLY, so an unrelated IOE thrown deeper inside SearchForNewManga
                        // (provider bug, malformed search input, etc.) is NOT silently swallowed
                        // here — it falls into the outer warn-and-log catch below where it can be
                        // diagnosed instead of mistaken for "no primary configured".
                        IMetadataSource primary = null;
                        try
                        {
                            var primaryDef = _metadataSourceFactory.GetPrimary();
                            primary = primaryDef != null
                                ? _metadataSourceFactory.GetInstance(primaryDef)
                                : null;
                        }
                        catch (InvalidOperationException)
                        {
                            // No primary metadata source configured (or factory has no providers
                            // wired). Fall through silently — cross-source resolution is best-effort.
                        }

                        if (primary != null)
                        {
                            try
                            {
                                var candidates = primary.SearchForNewManga(item.Title) ?? new List<Manga.Manga>();

                                // CodeRabbit review (strict-AND): when an item carries BOTH AniListId
                                // AND MalId, require BOTH to agree with the candidate. The
                                // OR-on-either pre-fix could pick a wrong candidate that happened to
                                // share only one ID (e.g. title-collision sibling that has same MalId
                                // but different AniListId). When an item carries only one of the IDs,
                                // the missing clause is short-circuited (no opinion). The outer if
                                // guarantees at least one ID is present so this never degenerates to
                                // "match any".
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
                    var monitored = importList.ShouldMonitor != MangaMonitor.None;

                    mangaToAdd.Add(new Manga.Manga
                    {
                        MangaDexId = mangaDexGuid,
                        MalId = item.MalId,
                        AniListId = item.AniListId,
                        Title = item.Title,
                        Monitored = monitored,
                        MonitorNewItems = importList.ShouldMonitor.DeriveMonitorNewItems(),
                        RootFolderPath = importList.RootFolderPath,
                        TranslationProfileId = importList.TranslationProfileId,
                        CustomFormatProfileId = importList.CustomFormatProfileId,
                        Tags = importList.Tags,
                        AddOptions = new AddMangaOptions
                        {
                            SearchForMissingChapters = importList.SearchForMissingChapters,

                            // #357: ShouldMonitor IS now a MangaMonitor — assign it directly,
                            // no remap. The old MonitorTypes->MangaMonitor block lossily coerced
                            // Existing/First to Latest because real Existing/First didn't exist;
                            // they do now (#357 D-3), so the user's choice flows through verbatim.
                            Monitor = importList.ShouldMonitor
                        }
                    });
                }
            }

            _addMangaService.AddManga(mangaToAdd, true);

            _logger.ProgressInfo("Import List Sync Completed. Items found: {0}, Manga added: {1}", items.Count, mangaToAdd.Count);
        }

        // The active primary's canonical-id family. We classify by Definition.Implementation
        // (the immutable class name) rather than the concrete instance type so the choice is
        // stable across user renames AND unit-testable without constructing a real
        // HttpMetadataSourceBase — this mirrors the WR-13 fix already used in
        // AddMangaService.ResolveCrossSourceIds.
        private enum PrimaryKind
        {
            Unknown,
            MangaDex,
            MangaBaka,
            AniList,
            MyAnimeList
        }

        // quick-260608-vf9 follow-up (#1) — CLOSED by #356/#357: for manga's flat chapter list the
        // Monitor choice already encodes new-chapter intent — any monitored ShouldMonitor selection
        // implies "keep monitoring chapters that appear later", and only None means "don't". The
        // per-manga new-chapter policy is now derived from ShouldMonitor via the shared
        // MangaMonitorExtensions.DeriveMonitorNewItems() helper (the per-class private copy was
        // removed). #356 removed the independent user-facing MonitorNewItems axis app-wide (manga
        // Edit modal + index column + API + locales); #357 unified ShouldMonitor onto the canonical
        // 7-value MangaMonitor.
        private static PrimaryKind ClassifyPrimary(MetadataSourceDefinition primaryDef)
        {
            if (primaryDef == null)
            {
                return PrimaryKind.Unknown;
            }

            return primaryDef.Implementation switch
            {
                nameof(MangaBakaMetadataSource) => PrimaryKind.MangaBaka,
                nameof(MangaDexMetadataSource) => PrimaryKind.MangaDex,
                nameof(AniListMetadataSource) => PrimaryKind.AniList,
                nameof(MyAnimeListMetadataSource) => PrimaryKind.MyAnimeList,
                _ => PrimaryKind.Unknown
            };
        }

        // Does this candidate carry the id field that AddMangaService.ResolveSourceIdForPrimary
        // requires for the active primary? (e.g. MangaBakaId when MangaBaka is primary.) Without
        // it, AddManga would throw "no source ID for active primary".
        private static bool HasPrimaryId(Manga.Manga candidate, PrimaryKind primaryKind) => primaryKind switch
        {
            PrimaryKind.MangaBaka => candidate.MangaBakaId.HasValue,
            PrimaryKind.MangaDex => candidate.MangaDexId.HasValue,
            PrimaryKind.AniList => candidate.AniListId.HasValue,
            PrimaryKind.MyAnimeList => candidate.MalId.HasValue,
            _ => false
        };

        // Does the raw import-list item already carry the active primary's OWN id? Import items
        // only ever carry MangaDexId/MalId/AniListId, so this is true only for a MyAnimeList primary
        // (item.MalId) or an AniList primary (item.AniListId) — a MangaBaka primary never matches
        // (items carry no MangaBakaId), so it always resolves via the primary's title search.
        private static bool ItemCarriesPrimaryId(ImportListItemInfo item, PrimaryKind primaryKind) => primaryKind switch
        {
            PrimaryKind.MyAnimeList => item.MalId.HasValue,
            PrimaryKind.AniList => item.AniListId.HasValue,
            _ => false
        };

        // Resolve a single import-list item to the active (non-MangaDex) primary's own id and
        // stage it for add. Import-list items only ever carry MangaDexId/MalId/AniListId (never
        // a MangaBakaId), so a non-MangaDex primary ALWAYS resolves via the primary's title
        // search, matching on the alt id(s) the item carries. The matched candidate from the
        // primary already carries the primary's id (e.g. MangaBakaId), which is exactly what the
        // downstream AddMangaService.PrepareForAdd needs.
        //
        // A MangaDexId-only item cannot resolve here: MangaBaka search results do not expose a
        // MangaDexId (MetadataSource D-03a), so there is nothing to match on — such an item is
        // skipped (MangaDex import lists belong under a MangaDex primary).
        private void StageViaPrimaryResolution(
            ImportListItemInfo item,
            ImportListDefinition importList,
            IMetadataSource primary,
            PrimaryKind primaryKind,
            List<Manga.Manga> mangaToAdd,
            List<ImportListExclusion> listExclusions,
            HashSet<int> existingMalIds,
            HashSet<int> existingAniListIds,
            List<string> existingMangaDexIds,
            ref bool crossSourceThrottled)
        {
            if (!item.MalId.HasValue && !item.AniListId.HasValue)
            {
                _logger.Debug("[{0}] Skipped — under a non-MangaDex primary the item carries no MAL/AniList id to resolve against the primary", item.Title);
                return;
            }

            // Short-circuit: already in the library by the alt id we carry. Avoids one primary
            // search per item per sync for titles we already own.
            if ((item.MalId.HasValue && existingMalIds.Contains(item.MalId.Value)) ||
                (item.AniListId.HasValue && existingAniListIds.Contains(item.AniListId.Value)))
            {
                _logger.Debug("[{0}] Rejected, already in library by alternate-ID (MAL={1}/AniList={2})", item.Title, item.MalId, item.AniListId);
                return;
            }

            Manga.Manga match;

            // Exact-id fast path (CodeRabbit #3377823326): when the active primary is MyAnimeList
            // or AniList, the item ALREADY carries the primary's own id (MalId / AniListId), so no
            // fuzzy title search is needed — and depending on one could drop a perfectly valid item
            // on a search miss or a 429. Stage directly from the id the item carries; AddMangaService
            // .PrepareForAdd then fetches the full record via GetMangaInfo(<primaryId>). MangaBaka
            // items never carry a MangaBakaId, so they always fall through to the title search below.
            if (ItemCarriesPrimaryId(item, primaryKind))
            {
                match = new Manga.Manga { MalId = item.MalId, AniListId = item.AniListId };
            }
            else
            {
                if (crossSourceThrottled || primary == null)
                {
                    return;
                }

                try
                {
                    var candidates = primary.SearchForNewManga(item.Title) ?? new List<Manga.Manga>();

                    // Strict-AND when the item carries both ids (mirrors the MangaDex-path resolver):
                    // a candidate must agree on every id the item actually carries.
                    match = candidates.FirstOrDefault(c =>
                        (!item.AniListId.HasValue || c.AniListId == item.AniListId) &&
                        (!item.MalId.HasValue || c.MalId == item.MalId));
                }
                catch (TooManyRequestsException)
                {
                    crossSourceThrottled = true;
                    _logger.Warn(
                        "Cross-source ID lookup against the primary metadata source throttled (HTTP 429); skipping resolution for the rest of this sync. Item [{0}] and subsequent items will be retried on the next scheduled sync.",
                        item.Title);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "[{0}] Cross-source resolution failed; item will be skipped", item.Title);
                    return;
                }
            }

            if (match == null || !HasPrimaryId(match, primaryKind))
            {
                _logger.Debug(
                    "[{0}] Rejected, the primary metadata source returned no match carrying its own id (MAL={1}/AniList={2})",
                    item.Title,
                    item.MalId,
                    item.AniListId);
                return;
            }

            var malId = item.MalId ?? match.MalId;
            var aniListId = item.AniListId ?? match.AniListId;
            var mangaDexIdString = match.MangaDexId?.ToString();

            // Exclusion check on the manga-ID quad — match on ANY populated id.
            // quick-260619-spc: also match match.MangaBakaId so a MangaBaka-keyed
            // exclusion rejects a candidate resolved under the v1.3 default-primary
            // (MangaBaka results carry MangaBakaId, never a MangaDexId — D-03a).
            var excluded = listExclusions.Any(s =>
                (mangaDexIdString != null && s.MangaDexId == mangaDexIdString) ||
                (malId.HasValue && s.MalId == malId) ||
                (aniListId.HasValue && s.AniListId == aniListId) ||
                (match.MangaBakaId.HasValue && s.MangaBakaId == match.MangaBakaId));

            if (excluded)
            {
                _logger.Debug("[{0}] Rejected due to list exclusion", item.Title);
                return;
            }

            // Library membership post-resolution: the match may surface ids the raw item lacked.
            if ((malId.HasValue && existingMalIds.Contains(malId.Value)) ||
                (aniListId.HasValue && existingAniListIds.Contains(aniListId.Value)) ||
                (mangaDexIdString != null && existingMangaDexIds.Contains(mangaDexIdString)))
            {
                _logger.Debug("[{0}] Rejected, manga exists in database (post-resolution)", item.Title);
                return;
            }

            // Batch dedup within this sync (two lists pointing at the same title).
            if (mangaToAdd.Any(m =>
                (match.MangaBakaId.HasValue && m.MangaBakaId == match.MangaBakaId) ||
                (malId.HasValue && m.MalId == malId) ||
                (aniListId.HasValue && m.AniListId == aniListId) ||
                (match.MangaDexId.HasValue && m.MangaDexId == match.MangaDexId)))
            {
                return;
            }

            var monitored = importList.ShouldMonitor != MangaMonitor.None;

            mangaToAdd.Add(new Manga.Manga
            {
                MangaBakaId = match.MangaBakaId,
                MangaDexId = match.MangaDexId,
                MalId = malId,
                AniListId = aniListId,
                Title = item.Title,
                Monitored = monitored,
                MonitorNewItems = importList.ShouldMonitor.DeriveMonitorNewItems(),
                RootFolderPath = importList.RootFolderPath,
                TranslationProfileId = importList.TranslationProfileId,
                CustomFormatProfileId = importList.CustomFormatProfileId,
                Tags = importList.Tags,
                AddOptions = new AddMangaOptions
                {
                    SearchForMissingChapters = importList.SearchForMissingChapters,

                    // #357: ShouldMonitor IS now a MangaMonitor — direct assignment, no remap.
                    Monitor = importList.ShouldMonitor
                }
            });

            _logger.Debug(
                "[{0}] Resolved via primary ({1}) → staged for add (MangaBakaId={2}, MAL={3}, AniList={4})",
                item.Title,
                primaryKind,
                match.MangaBakaId,
                malId,
                aniListId);
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
