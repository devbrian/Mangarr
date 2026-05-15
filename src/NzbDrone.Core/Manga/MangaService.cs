using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Organizer.Manga;
using NzbDrone.Core.Parser.Manga;

namespace NzbDrone.Core.Manga
{
    // Service implementation for Manga aggregate. Mirrors Sonarr's SeriesService
    // (Tv/SeriesService.cs:39-313) event-publish-after-insert pattern verbatim, slimmed
    // for Phase 2 deliverables (no IBuildSeriesPaths, no IAutoTaggingService — those
    // are Phase 5+ territory per 02-CONTEXT Out-of-Phase 2).
    public class MangaService : IMangaService
    {
        private readonly IMangaRepository _mangaRepository;
        private readonly IEventAggregator _eventAggregator;
        private readonly IBuildMangaPaths _mangaPathBuilder;
        private readonly Logger _logger;

        public MangaService(IMangaRepository mangaRepository,
                            IEventAggregator eventAggregator,
                            IBuildMangaPaths mangaPathBuilder,
                            Logger logger)
        {
            _mangaRepository = mangaRepository;
            _eventAggregator = eventAggregator;
            _mangaPathBuilder = mangaPathBuilder;
            _logger = logger;
        }

        // BL-01 fix: Find returns null on missing (Get throws ModelNotFoundException),
        // so the controller's `Results<Accepted, NotFound>` declared NotFound branch
        // is now reachable. Callers that need the "must exist" guarantee should call
        // _mangaRepository.Get directly or handle the null themselves.
        public Manga GetManga(int mangaId)
        {
            return _mangaRepository.Find(mangaId);
        }

        public List<Manga> GetManga(IEnumerable<int> mangaIds)
        {
            return _mangaRepository.Get(mangaIds).ToList();
        }

        public Manga AddManga(Manga newManga)
        {
            _mangaRepository.Insert(newManga);
            _eventAggregator.PublishEvent(new MangaAddedEvent(GetManga(newManga.Id)));

            return newManga;
        }

        // Phase 8 audit gap-10 (SeriesService-vs-MangaService.md): bulk-add publishes
        // ONE MangaImportedEvent carrying all newly-added ids — mirrors Tv/SeriesService
        // .AddSeries(List<Series>) at line 81-87. Replaces the prior N-event-per-add
        // loop (one MangaAddedEvent per manga) which fanned out N RefreshMangaCommand
        // pushes; MangaAddedHandler.Handle(MangaImportedEvent) now batches them into
        // one PushMany. Single-add path (AddManga(Manga) above) keeps publishing
        // MangaAddedEvent for backwards compat with existing single-item consumers.
        public List<Manga> AddManga(List<Manga> newManga)
        {
            _mangaRepository.InsertMany(newManga);
            _eventAggregator.PublishEvent(new MangaImportedEvent(newManga.Select(m => m.Id).ToList()));

            return newManga;
        }

        public Manga FindByMangaDexId(Guid mangaDexId)
        {
            return _mangaRepository.FindByMangaDexId(mangaDexId);
        }

        public Manga FindByMalId(int malId)
        {
            return _mangaRepository.FindByMalId(malId);
        }

        public Manga FindByAniListId(int aniListId)
        {
            return _mangaRepository.FindByAniListId(aniListId);
        }

        public Manga FindByTitle(string title)
        {
            return _mangaRepository.FindByTitle(title);
        }

        // Phase 8 audit gap-02 (SeriesService-vs-MangaService.md): year-disambiguating
        // overload mirrors Tv/SeriesService.FindByTitle(string, int) at line 158-161.
        // Same-title collisions on the manga axis (remakes, alternative scanlations)
        // are real — PublicationYear lives on Manga.cs:79 and Parser disambiguation
        // needs this. Normalizes the input title via MangaTitleNormalizer (D-05's
        // single source of truth) then delegates to IMangaRepository.FindByTitle,
        // which lower-cases the normalized result and filters on PublicationYear.
        public Manga FindByTitle(string title, int year)
        {
            return _mangaRepository.FindByTitle(MangaTitleNormalizer.Normalize(title), year);
        }

        // Phase 8 audit gap-03 (SeriesService-vs-MangaService.md): service-level wrapper
        // for the parser's fuzzy substring fallback path. Mirrors the SHAPE of TV's
        // SeriesService.FindByTitleInexact (Tv/SeriesService.cs:109-151) but returns
        // the full candidate list (not the leftmost-longest single pick) — manga
        // signature diverges to give callers (parser fallback, future disambiguation
        // UI) all matches so they can apply domain-aware selection (PublicationYear /
        // PrimaryAuthor confirms per D-21). Normalizes the input title via
        // MangaTitleNormalizer (D-05's single source of truth) before delegating to
        // IMangaRepository.FindByTitleInexact. Empty / null / whitespace input yields
        // an empty list (the repository BL-02 guard returns empty on missing input).
        // GH #118 — service-level wrapper for the parser's alt-title resolution
        // path. Strategy 2 of MangaParsingService.GetManga multi-strategy
        // resolution (Sonarr-canonical mirror of ParsingService.GetSeries).
        // Normalizes the input title via MangaTitleNormalizer (D-05 single
        // source of truth) before delegating to IMangaRepository — same
        // canonicalization the metadata sources used at write-time, so the
        // comparison is between two equally-normalized strings.
        public Manga FindByAlternativeTitle(string title)
        {
            return _mangaRepository.FindByAlternativeTitle(MangaTitleNormalizer.Normalize(title));
        }

        public List<Manga> FindByTitleInexact(string title)
        {
            var cleanTitle = MangaTitleNormalizer.Normalize(title);
            var list = _mangaRepository.FindByTitleInexact(cleanTitle);

            if (list.Count > 1)
            {
                _logger.Debug("Multiple manga matched substring of title {0}", title);
                foreach (var entry in list)
                {
                    _logger.Debug("Multiple manga match candidate: {0} cleantitle: {1}", entry.Title, entry.CleanTitle);
                }
            }

            return list;
        }

        public Manga FindByPath(string path)
        {
            return _mangaRepository.FindByPath(path);
        }

        // BL-09 fix: BasicRepository.Get(IEnumerable<int>) throws if ANY id is missing,
        // aborting the entire batch with a 500. Filter to the ids that actually exist
        // first so partial-batch deletes succeed and missing ids are silently skipped
        // (matches Sonarr's tolerant Delete shape — a 404-on-each is the controller's
        // job, not the service's).
        public void DeleteManga(List<int> mangaIds, bool deleteFiles)
        {
            var mangaList = _mangaRepository.All()
                .Where(m => mangaIds.Contains(m.Id))
                .ToList();

            foreach (var manga in mangaList)
            {
                _mangaRepository.Delete(manga);
                _eventAggregator.PublishEvent(new MangaDeletedEvent(manga, deleteFiles));
            }
        }

        public List<Manga> GetAllManga()
        {
            return _mangaRepository.All().ToList();
        }

        // Mirrors Tv/SeriesService.AllForTag (Tv/SeriesService.cs:195) verbatim.
        // Closes Phase 8 audit gap-04: Settings/Tags UI manga-using-tag count + tag delete flow.
        public List<Manga> AllForTag(int tagId)
        {
            return GetAllManga().Where(m => m.Tags.Contains(tagId))
                                .ToList();
        }

        public List<int> AllMangaIds()
        {
            return _mangaRepository.All().Select(m => m.Id).ToList();
        }

        // Phase 8 audit gap-11 (SeriesService-vs-MangaService.md): manga peers of TV's
        // SeriesService.AllSeriesTvdbIds() (Tv/SeriesService.cs:175). Exposes the
        // cross-source ID lists for ImportList "skip-already-added" filters and
        // ImportListExclusion bookkeeping (D-16). Manga diverges from TV's single
        // TvdbId column by carrying three optional cross-source IDs (MangaDexId/MalId/
        // AniListId), so we expose three peer methods. Repository layer (MangaRepository
        // .AllMangaDexIds/AllMalIds/AllAniListIds) already filters out null values.
        public List<Guid> AllMangaDexIds()
        {
            return _mangaRepository.AllMangaDexIds();
        }

        public List<int> AllMalIds()
        {
            return _mangaRepository.AllMalIds();
        }

        public List<int> AllAniListIds()
        {
            return _mangaRepository.AllAniListIds();
        }

        public Dictionary<int, string> GetAllMangaPaths()
        {
            return _mangaRepository.AllMangaPaths();
        }

        // Phase 8 audit gap-05 (SeriesService-vs-MangaService.md): mirrors TV's
        // SeriesService.GetAllSeriesTags (Tv/SeriesService.cs:185). Returns mangaId →
        // tag list for bulk tag operations / housekeeping cleanup. Wraps
        // IMangaRepository.AllMangaTags.
        public Dictionary<int, List<int>> GetAllMangaTags()
        {
            return _mangaRepository.AllMangaTags();
        }

        // Phase 10 Plan 10-07 (FINDINGS Open Q 5 close-out): existing 2-arg overload
        // REWIRED to delegate to the new 3-arg overload below with
        // triggerSeriesEdited: false. The Phase 8 audit gap-09 close-out behavior
        // (publish MangaEditedEvent on the USER-EDIT path) is now driven by the
        // MangaController PUT call site explicitly opting INTO triggerSeriesEdited
        // via the 3-arg overload (Option B locked per W4 revision). All other
        // 2-arg callers (RefreshMangaService passes publishUpdatedEvent=false;
        // MoveMangaService and MangaLinksController default to publishUpdatedEvent=true)
        // now emit MangaUpdatedEvent on the 2-arg path; only the explicit user-edit
        // PUT entry point emits both events.
        public Manga UpdateManga(Manga manga, bool publishUpdatedEvent = true)
        {
            return UpdateManga(manga, publishUpdatedEvent, triggerSeriesEdited: false);
        }

        // Sonarr divergence: Phase 10 Plan 10-07 (FINDINGS Open Q 5 close-out — UI
        // single-edit path missing MangaEditedEvent publish on top of MangaUpdatedEvent)
        // — see DIVERGENCE.md.
        // Role-match analog: Tv/SeriesService.cs UpdateSeries(Series, bool, bool) at
        // lines 203-234. TV's analog has only one event publish (SeriesEditedEvent
        // gated by publishUpdatedEvent); the manga 3-arg variant introduces a second
        // bool (triggerSeriesEdited) so the controller-PUT call site can opt into
        // BOTH MangaUpdatedEvent + MangaEditedEvent atomically. Phase 14 will collapse
        // the two-bool shape into TV's single-bool shape if/when the manga aggregate
        // becomes the canonical update entry point.
        //
        // Why two booleans: TV semantics — publishUpdatedEvent gates MangaUpdatedEvent
        // (any update, incl. metadata refresh); triggerSeriesEdited gates
        // MangaEditedEvent (user-explicit edit only). RefreshMangaService bypasses
        // both via the 2-arg overload's publishUpdatedEvent=false path; the
        // MangaController PUT endpoint passes both true so MangaController's
        // IHandle<MangaEditedEvent> (Plan 10-05) and IHandle<MangaUpdatedEvent>
        // both fire on the UI single-edit path.
        //
        // Pitfall 4 ordering: DB Update FIRST, MangaUpdatedEvent SECOND, MangaEditedEvent
        // LAST. Subscribers reading manga state via the repository see committed
        // values for both event handlers.
        //
        // BL-09 fix preservation: the no-op-update guard (Find-first → throw
        // ModelNotFoundException if missing) is preserved verbatim — Dapper's
        // UPDATE ... WHERE Id silently no-ops on missing rows, then SignalR would
        // broadcast a phantom event the UI then refetches and 404s on.
        //
        // Phase 14 cleanup: rename `triggerSeriesEdited` parameter to `triggerMangaEdited`
        // when Tv/ deletes; preserve method behavior verbatim.
        public Manga UpdateManga(Manga manga, bool publishUpdatedEvent, bool triggerSeriesEdited)
        {
            if (manga == null)
            {
                throw new ArgumentNullException(nameof(manga));
            }

            // Capture pre-edit snapshot for the MangaEditedEvent payload + BL-09
            // existence guard.
            var stored = _mangaRepository.Find(manga.Id);

            if (stored == null)
            {
                throw new ModelNotFoundException(typeof(Manga), manga.Id);
            }

            // 1. DB Update FIRST.
            var updated = _mangaRepository.Update(manga);

            // 2. THEN MangaUpdatedEvent (any-update signal). Sonarr's
            //    IEventAggregator.PublishEvent is synchronous fan-out — subscribers
            //    see the committed DB row when they query the repository in response
            //    to either event.
            if (publishUpdatedEvent)
            {
                _eventAggregator.PublishEvent(new MangaUpdatedEvent(updated));
            }

            // 3. LAST, MangaEditedEvent (user-explicit-edit signal). Carries the
            //    pre-edit snapshot so handlers can diff (path change → move,
            //    monitor flip → refresh, etc.).
            if (triggerSeriesEdited)
            {
                _eventAggregator.PublishEvent(new MangaEditedEvent(updated, stored));
            }

            return updated;
        }

        // Phase 8 audit gap-01 (SeriesService-vs-MangaService.md): bulk-edit fan-out
        // mirroring Tv/SeriesService.UpdateSeries(List<Series>, bool) at line 236-263.
        // Per-item path rebuild via IBuildMangaPaths when RootFolderPath is set,
        // single UpdateMany round-trip, single MangaBulkEditedEvent publish at the
        // end. Intentionally diverges from the single-item UpdateManga in two ways:
        //   1. No per-item Find existence guard — bulk callers (V5 MangaController
        //      PUT /editor) pre-load via GetManga(IEnumerable<int>), so the rows
        //      are guaranteed to exist; a per-item Find here would just double
        //      the read traffic for the same answer.
        //   2. Always publishes MangaBulkEditedEvent (no opt-out flag) — the bulk
        //      path has only one consumer shape (post-edit fan-out: rename, move,
        //      refresh) and there is no parity for the RefreshMangaService
        //      "suppress all events" carve-out the single-item path needs.
        // No IAutoTaggingService.UpdateTags call — auto-tagging is Phase 5+ territory
        // per Manga/CLAUDE.md "Phase 2 Out-of-Phase 2" (matches the omission in the
        // single-item UpdateManga at line 216-238).
        public List<Manga> UpdateManga(List<Manga> manga, bool useExistingRelativeFolder)
        {
            _logger.Debug("Updating {0} manga", manga.Count);

            foreach (var m in manga)
            {
                _logger.Trace("Updating: {0}", m.Title);

                if (!m.RootFolderPath.IsNullOrWhiteSpace())
                {
                    m.Path = _mangaPathBuilder.BuildPath(m, useExistingRelativeFolder);

                    _logger.Trace("Changing path for {0} to {1}", m.Title, m.Path);
                }
                else
                {
                    _logger.Trace("Not changing path for: {0}", m.Title);
                }
            }

            _mangaRepository.UpdateMany(manga);
            _logger.Debug("{0} manga updated", manga.Count);
            _eventAggregator.PublishEvent(new MangaBulkEditedEvent(manga));

            return manga;
        }

        public bool MangaPathExists(string folder)
        {
            return _mangaRepository.MangaPathExists(folder);
        }

        // Mirrors Tv/SeriesService.RemoveAddOptions (Tv/SeriesService.cs:270): clears
        // Manga.AddOptions and persists via SetFields so only the AddOptions column is
        // written without firing MangaUpdatedEvent. Closes Phase 8 audit gap-07.
        public void RemoveAddOptions(Manga manga)
        {
            manga.AddOptions = null;
            _mangaRepository.SetFields(manga, m => m.AddOptions);
        }
    }
}
