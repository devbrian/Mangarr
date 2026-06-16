using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentValidation;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.MetadataSource.MangaBaka;
using NzbDrone.Core.MetadataSource.MangaDex;
using NzbDrone.Core.MetadataSource.MyAnimeList;
using NzbDrone.Core.Organizer.Manga;
using NzbDrone.Core.Parser.Manga;

namespace NzbDrone.Core.Manga
{
    /// <summary>
    /// META-02 AddManga orchestration mirroring Mangarr's <c>AddSeriesService</c> (DELETED Phase 15).
    ///
    /// <para>Pipeline:
    /// 1. Reject duplicates by any populated cross-source ID
    /// 2. Resolve primary metadata via dynamic primary (D-15) — populates fields + may
    ///    surface MangaDex links.al/links.mal cross-source IDs
    /// 2.5. PER D-19: validate every cross-source ID populated from the primary's links
    ///    field by fetching the linked entity from the secondary provider and re-running
    ///    the full D-21 gate. On failure, RESET the ID to null and emit warning log
    ///    "D-19 validation failed".
    /// 3. PER D-20..D-22: cross-source ID resolution. Includes the explicit reverse-MangaDex
    ///    branch — when primary != MangaDex AND MangaDexId is null, run
    ///    <c>MangaDex.SearchForNewManga(title)</c> and pick the highest-similarity
    ///    D-21-passing hit per D-20. Generic fallback fills remaining missing IDs from any
    ///    other secondary that exposes a search-by-title surface.
    /// 4. Compute clean/sort titles via <see cref="MangaTitleNormalizer.Normalize"/> (D-05)
    /// 5. Persist via <see cref="IMangaService.AddManga(Manga)"/> (which publishes
    ///    <see cref="Events.MangaAddedEvent"/>)
    /// 6. Synthesize / sync chapters via <see cref="IChapterListService"/> (D-17)
    /// 7. Initial <c>RefreshMangaCommand</c> (IsNewManga=true) is dispatched by
    ///    <see cref="MangaAddedHandler"/> via the published <see cref="Events.MangaAddedEvent"/>
    ///    (Phase 8 audit gap-01 — restored Mangarr's IHandle pattern; no longer inlined here).
    /// </para>
    /// </summary>
    public class AddMangaService : IAddMangaService
    {
        private readonly IMangaService _mangaService;
        private readonly IMetadataSourceFactory _metaFactory;
        private readonly IChapterListService _chapterListService;
        private readonly IBuildMangaFileNames _fileNameBuilder;
        private readonly CrossSourceIdResolver _resolver;
        private readonly IAddMangaValidator _addMangaValidator;
        private readonly Logger _logger;

        public AddMangaService(IMangaService mangaService,
                               IMetadataSourceFactory metaFactory,
                               IChapterListService chapterListService,
                               IBuildMangaFileNames fileNameBuilder,
                               CrossSourceIdResolver resolver,
                               IAddMangaValidator addMangaValidator,
                               Logger logger)
        {
            _mangaService = mangaService;
            _metaFactory = metaFactory;
            _chapterListService = chapterListService;
            _fileNameBuilder = fileNameBuilder;
            _resolver = resolver;
            _addMangaValidator = addMangaValidator;
            _logger = logger;
        }

        public Manga AddManga(Manga newManga)
        {
            if (newManga == null)
            {
                throw new ArgumentNullException(nameof(newManga));
            }

            PrepareForAdd(newManga);

            // 5. Persist + publish (MangaService.AddManga publishes MangaAddedEvent).
            var added = _mangaService.AddManga(newManga);

            // 6. Phase 16.1 (post-revert): chapter-list synthesis is performed by
            //    RefreshMangaService through a single-pass IChapterListService.SyncChapters
            //    (mirrors Sonarr's IRefreshEpisodeService.RefreshEpisodeInfo). No inline
            //    synthesis step here. The initial RefreshMangaCommand fires via
            //    MangaAddedHandler subscribing to the published MangaAddedEvent — the
            //    Refresh path lands the canonical Chapter rows on the Sonarr-canonical
            //    (MangaId, ChapterNumber) grain. Per-translation data lands on ChapterFile
            //    post-import (Phase 6 PIPELINE-04 + Phase 16.1 D-04 — Sonarr-canonical
            //    pattern; mirrors EpisodeFile.Languages placement).
            //
            // 7. Initial RefreshMangaCommand (IsNewManga=true) is dispatched by
            //    MangaAddedHandler via the published MangaAddedEvent (Phase 8 audit
            //    gap-01 — Mangarr's SeriesAddedHandler pattern restored; no inline push).

            return added;
        }

        // Phase 8 audit gap-01 (AddSeriesService-vs-AddMangaService.md): bulk add overload
        // mirroring Tv/AddSeriesService.cs:57-112 (`AddSeries(List<Series>, bool ignoreErrors)`).
        // Used by future ImportLists pipeline (v2 IMP-01..03) + bulk-add UI flow. Per-item
        // runs the same metadata-fetch + cross-source-resolution + validation pipeline as
        // the single-add path; dedups against existing manga (by MangaDex/MAL/AniList ID)
        // and against the in-progress batch; tolerates ValidationException per item when
        // ignoreErrors=true. Persists via the bulk IMangaService.AddManga(List<Manga>) so
        // a single InsertMany roundtrip lands the batch — chapter-list synthesis is deferred
        // to the per-item RefreshMangaCommand fired by MangaAddedHandler via MangaAddedEvent
        // (the same path Mangarr's SeriesAddedHandler walks for TV).
        //
        // gap-02 (TitleSlug bulk dedup) is intentionally NOT implemented here — manga has
        // no TitleSlug field yet (Series-vs-Manga audit gap-04 territory); this method
        // becomes the obvious anchor for that check once TitleSlug ships.
        public List<Manga> AddManga(List<Manga> newManga, bool ignoreErrors = false)
        {
            var added = DateTime.UtcNow;
            var mangaToAdd = new List<Manga>();
            var existingMangaDexIds = new HashSet<Guid>(_mangaService.GetAllManga()
                .Where(m => m.MangaDexId.HasValue)
                .Select(m => m.MangaDexId.Value));
            var existingMalIds = new HashSet<int>(_mangaService.GetAllManga()
                .Where(m => m.MalId.HasValue)
                .Select(m => m.MalId.Value));
            var existingAniListIds = new HashSet<int>(_mangaService.GetAllManga()
                .Where(m => m.AniListId.HasValue)
                .Select(m => m.AniListId.Value));
            var existingMangaBakaIds = new HashSet<int>(_mangaService.GetAllManga()
                .Where(m => m.MangaBakaId.HasValue)
                .Select(m => m.MangaBakaId.Value));

            foreach (var m in newManga)
            {
                if (string.IsNullOrWhiteSpace(m.Path))
                {
                    _logger.Info("Adding Manga {0} Root Folder Path: [{1}]", m.Title, m.RootFolderPath);
                }
                else
                {
                    _logger.Info("Adding Manga {0} Path: [{1}]", m.Title, m.Path);
                }

                try
                {
                    PrepareForAdd(m);
                    m.Added = added;

                    // Mirror Tv/AddSeriesService.cs:79-89 — drop already-existing primary IDs
                    // (TVDB on TV; any populated cross-source ID on manga). Includes both
                    // the persisted-in-DB check AND the in-progress-batch dedup. The persisted
                    // check is best-effort (the single-add path's per-ID Find* lookups in
                    // PrepareForAdd already throw InvalidOperationException on collision); the
                    // batch-dedup catches the multi-list-source case where two import-list
                    // entries point at the same primary ID.
                    if (m.MangaDexId.HasValue && existingMangaDexIds.Contains(m.MangaDexId.Value))
                    {
                        _logger.Debug("MangaDex ID {0} was not added — manga {1} already exists in database", m.MangaDexId, m.Title);
                        continue;
                    }

                    if (m.MalId.HasValue && existingMalIds.Contains(m.MalId.Value))
                    {
                        _logger.Debug("MAL ID {0} was not added — manga {1} already exists in database", m.MalId, m.Title);
                        continue;
                    }

                    if (m.AniListId.HasValue && existingAniListIds.Contains(m.AniListId.Value))
                    {
                        _logger.Debug("AniList ID {0} was not added — manga {1} already exists in database", m.AniListId, m.Title);
                        continue;
                    }

                    if (m.MangaBakaId.HasValue && existingMangaBakaIds.Contains(m.MangaBakaId.Value))
                    {
                        _logger.Debug("MangaBaka ID {0} was not added — manga {1} already exists in database", m.MangaBakaId, m.Title);
                        continue;
                    }

                    if (m.MangaDexId.HasValue && mangaToAdd.Any(f => f.MangaDexId == m.MangaDexId))
                    {
                        _logger.Trace("MangaDex ID {0} was already added from another import list, not adding manga {1} again", m.MangaDexId, m.Title);
                        continue;
                    }

                    if (m.MalId.HasValue && mangaToAdd.Any(f => f.MalId == m.MalId))
                    {
                        _logger.Trace("MAL ID {0} was already added from another import list, not adding manga {1} again", m.MalId, m.Title);
                        continue;
                    }

                    if (m.AniListId.HasValue && mangaToAdd.Any(f => f.AniListId == m.AniListId))
                    {
                        _logger.Trace("AniList ID {0} was already added from another import list, not adding manga {1} again", m.AniListId, m.Title);
                        continue;
                    }

                    if (m.MangaBakaId.HasValue && mangaToAdd.Any(f => f.MangaBakaId == m.MangaBakaId))
                    {
                        _logger.Trace("MangaBaka ID {0} was already added from another import list, not adding manga {1} again", m.MangaBakaId, m.Title);
                        continue;
                    }

                    mangaToAdd.Add(m);
                }
                catch (ValidationException ex)
                {
                    if (!ignoreErrors)
                    {
                        throw;
                    }

                    _logger.Debug("Manga {0} (MangaDex ID {1}, MAL {2}, AniList {3}) was not added due to validation failures. {4}",
                        m.Title,
                        m.MangaDexId,
                        m.MalId,
                        m.AniListId,
                        ex.Message);
                }
            }

            return _mangaService.AddManga(mangaToAdd);
        }

        // Extract of the single-add prep pipeline (steps 1-4 + path/AddOptions/validator)
        // shared by AddManga(Manga) and the bulk AddManga(List<Manga>, bool) overload.
        //
        // Phase 16.1 (post-revert): chapter-list synthesis is no longer performed inline
        // at add time. The per-item RefreshMangaCommand fires via MangaAddedHandler
        // subscribing to MangaAddedEvent and lands the canonical Chapter rows through a
        // single-pass IChapterListService.SyncChapters (mirrors Sonarr's
        // IRefreshEpisodeService.RefreshEpisodeInfo). Per-translation data lands on
        // ChapterFile post-import (Phase 16.1 D-04 — Sonarr-canonical pattern). Returning
        // Manga only (was a (Manga, List<Chapter>) tuple pre-Phase-16) keeps the call site
        // honest about what we use.
        private Manga PrepareForAdd(Manga newManga)
        {
            // 1. Reject duplicates by any populated cross-source ID.
            if (newManga.MangaDexId.HasValue && _mangaService.FindByMangaDexId(newManga.MangaDexId.Value) != null)
            {
                throw new InvalidOperationException($"Manga with MangaDex ID {newManga.MangaDexId} already exists");
            }

            if (newManga.MalId.HasValue && _mangaService.FindByMalId(newManga.MalId.Value) != null)
            {
                throw new InvalidOperationException($"Manga with MAL ID {newManga.MalId} already exists");
            }

            if (newManga.AniListId.HasValue && _mangaService.FindByAniListId(newManga.AniListId.Value) != null)
            {
                throw new InvalidOperationException($"Manga with AniList ID {newManga.AniListId} already exists");
            }

            // MangaBaka is the v1.3 default primary source; its entries frequently lack a
            // MangaDex/MAL/AniList cross-link, so MangaBakaId is the only dedup anchor for
            // those titles. FindByMangaBakaId exists on IMangaService (the MangaLinksController
            // collision guard already uses it). Without this guard, widening the controller
            // PostValidator to accept MangaBakaId would let the same MangaBaka-only manga be
            // added twice.
            if (newManga.MangaBakaId.HasValue && _mangaService.FindByMangaBakaId(newManga.MangaBakaId.Value) != null)
            {
                throw new InvalidOperationException($"Manga with MangaBaka ID {newManga.MangaBakaId} already exists");
            }

            // 2. Resolve primary metadata.
            var primaryDef = _metaFactory.GetPrimary();
            var primary = (IProvideMangaInfo)_metaFactory.GetInstance(primaryDef);
            var primarySourceId = ResolveSourceIdForPrimary(newManga, primary);
            var primaryTuple = primary.GetMangaInfo(primarySourceId);
            var primaryManga = primaryTuple.Item1;

            // Phase 8 cluster-03 cascade: TV's pattern is `series.ApplyChanges(newSeries)`
            // (metadata ← user). Manga's call direction is reversed (newManga ← primaryManga),
            // so since Plan 03-05 expanded ApplyChanges to copy user-owned fields
            // (RootFolderPath, Path, Tags, Monitored, AddOptions, TranslationProfileId,
            // CustomFormatProfileId) per audit gap-02 + issue #28 backfill, those user inputs
            // would now get clobbered by the metadata's nulls. Save user inputs across the call.
            // MonitorNewItems is NO LONGER user-owned (#356) — it is derived from the Monitor
            // choice after ApplyChanges (see below). Phase 9 should restructure PrepareForAdd
            // to use TV's call ordering.
            var userRootFolderPath = newManga.RootFolderPath;
            var userPath = newManga.Path;
            var userTags = newManga.Tags;
            var userMonitored = newManga.Monitored;
            var userAddOptions = newManga.AddOptions;
            var userTranslationProfileId = newManga.TranslationProfileId;
            var userCustomFormatProfileId = newManga.CustomFormatProfileId;

            newManga.ApplyChanges(primaryManga);

            newManga.RootFolderPath = userRootFolderPath ?? newManga.RootFolderPath;
            newManga.Path = userPath ?? newManga.Path;
            newManga.Tags = userTags ?? newManga.Tags;
            newManga.Monitored = userMonitored;
            newManga.AddOptions = userAddOptions ?? newManga.AddOptions;
            newManga.TranslationProfileId = userTranslationProfileId;
            newManga.CustomFormatProfileId = userCustomFormatProfileId;

            // #356 D-1/D-4: MonitorNewItems is DERIVED from the Monitor choice (None->None,
            // else->All), never echoed from user input. The explicit `?? MangaMonitor.All`
            // keeps the null-AddOptions path at "monitor new chapters" (Sonarr default) so it
            // never depends on default(MangaMonitor) (now None=0).
            newManga.MonitorNewItems = (newManga.AddOptions?.Monitor ?? MangaMonitor.All).DeriveMonitorNewItems();

            // #320: the AddManga modal sends profileId == 0 (the int default) when no profile is
            // picked and none is the global default. `0` is a non-existent FK — profile rows start
            // at id 1 — so persisting it makes every downstream consumer special-case it, and it was
            // the upstream trigger for the import wedge in #318. The canonical "use the seeded default"
            // sentinel is NULL: every consumer resolves `Manga.<X>ProfileId ?? Config.Default<X>ProfileId`,
            // and both profile services seed Config.Default*ProfileId to the Default profile on first
            // run (TranslationProfileService.cs:112 / CustomFormatProfileService.cs:135). Coerce the
            // 0 sentinel to NULL here — the single server-side chokepoint for every add path — so 0
            // never reaches persistence and the runtime fallback resolves to the real Default profile.
            if (newManga.TranslationProfileId == 0)
            {
                newManga.TranslationProfileId = null;
            }

            if (newManga.CustomFormatProfileId == 0)
            {
                newManga.CustomFormatProfileId = null;
            }

            // Carry over the primary IDs returned by GetMangaInfo (incl. any links extracted by
            // MapManga, e.g. MangaDex links.al/mal, or the MangaBaka source_links cross-refs).
            newManga.MangaBakaId ??= primaryManga.MangaBakaId;
            newManga.MangaDexId ??= primaryManga.MangaDexId;
            newManga.MalId ??= primaryManga.MalId;
            newManga.AniListId ??= primaryManga.AniListId;

            // 2.5. PER D-19: VALIDATE every cross-source ID populated from a primary.links field by
            //      fetching the linked entity from the corresponding secondary provider and confirming
            //      title similarity >=0.85 against MangaTitleNormalizer-normalized candidates (full D-21
            //      gate including multi-axis confirm). If validation FAILS, reset that ID to null and
            //      log "D-19 validation failed" so the cross-source-resolver fallback (step 3) re-runs
            //      via fuzzy title-search instead of trusting the unvalidated link.
            ValidateLinkedCrossSourceIds(newManga, primaryManga);

            // 3. Cross-source ID resolution per D-19..D-22 (best-effort; failures stay null per D-23).
            //    Symmetric per D-20: when primary is NOT MangaDex AND MangaDexId is missing, run
            //    fuzzy title match against MangaDex /manga?title=... and pick the highest-similarity
            //    result clearing the D-21 gate (handled inside ResolveCrossSourceIds).
            ResolveCrossSourceIds(newManga, primaryManga);

            // Phase 8 audit gap-03 (AddSeriesService-vs-AddMangaService.md): mirror
            // Tv/AddSeriesService.cs:144-148 — when the Add Manga UI submits without a
            // pre-computed Path (user picked just RootFolder + title), default it to
            // <RootFolderPath>/<GetMangaFolder>. Placed BEFORE clean/sort title compute
            // so any future Path/TitleSlug validators (gap-04) see the resolved value.
            if (string.IsNullOrWhiteSpace(newManga.Path))
            {
                var folderName = _fileNameBuilder.GetMangaFolder(newManga);
                newManga.Path = Path.Combine(newManga.RootFolderPath, folderName);
            }

            // 4. Compute clean/sort titles via the single normalizer (D-05).
            newManga.CleanTitle = MangaTitleNormalizer.Normalize(newManga.Title);
            newManga.SortTitle = newManga.CleanTitle;

            // Phase 8 audit gap-04 (Series-vs-Manga.md): TitleSlug parity. TV's
            // Series.TitleSlug arrives pre-computed from SkyHook (RefreshSeriesService:91);
            // manga's primary metadata sources don't expose a slug field, so we derive
            // it locally via StringExtensions.ToUrlSlug() — the same extension Mangarr ships.
            // Frontend /manga/:titleSlug route at MangaDetailsPage.tsx:28-31 + the
            // MangaIndexOverview link at line 114 require this end-to-end.
            newManga.TitleSlug = newManga.Title.ToUrlSlug();
            newManga.Added = DateTime.UtcNow;

            // Phase 8 audit gap-05 (AddSeriesService-vs-AddMangaService.md): mirror
            // Tv/AddSeriesService.cs:154-157 — when the user explicitly chose Monitor=None
            // on the Add Manga dialog, force Monitored=false so the post-add chapter monitor
            // cascade respects the choice.
            if (newManga.AddOptions != null && newManga.AddOptions.Monitor == MangaMonitor.None)
            {
                newManga.Monitored = false;
            }

            // Phase 8 audit gap-04 (AddSeriesService-vs-AddMangaService.md): mirror
            // Tv/AddSeriesService.cs:159-164 — run FluentValidation via IAddMangaValidator
            // (RootFolderValidator + IsValidPath today; cluster-05 will add MangaPath/
            // Ancestor/TitleSlug peers). Throws ValidationException on failure so the
            // Add Manga UI surfaces a typed error instead of persisting bad input.
            var validationResult = _addMangaValidator.Validate(newManga);

            if (!validationResult.IsValid)
            {
                throw new ValidationException(validationResult.Errors);
            }

            return primaryManga;
        }

        private static string ResolveSourceIdForPrimary(Manga manga, IProvideMangaInfo primary)
        {
            var sourceId = primary switch
            {
                MangaBakaMetadataSource _ => manga.MangaBakaId?.ToString(),
                MangaDexMetadataSource _ => manga.MangaDexId?.ToString(),
                AniListMetadataSource _ => manga.AniListId?.ToString(),
                MyAnimeListMetadataSource _ => manga.MalId?.ToString(),
                _ => throw new InvalidOperationException(
                    $"Unknown primary metadata source type {primary?.GetType().Name}")
            };

            return sourceId
                ?? throw new ArgumentException(
                    $"newManga has no source ID for active primary {primary.GetType().Name}");
        }

        // PER D-19: For every cross-source ID that came from the primary's links field
        // (e.g. MangaDex attributes.links.al/mal), fetch the linked entity from the secondary
        // provider and re-run the full D-21 gate. If the link does NOT validate, RESET the ID
        // to null + log "D-19 validation failed" so the fuzzy-fallback path (step 3) takes over.
        private void ValidateLinkedCrossSourceIds(Manga newManga, Manga primaryResult)
        {
            var primaryCandidate = ToCandidate(primaryResult);

            foreach (var def in _metaFactory.All().Where(d => !d.IsPrimary))
            {
                var src = (IProvideMangaInfo)_metaFactory.GetInstance(def);

                // AniList link validation.
                if (newManga.AniListId.HasValue && src is AniListMetadataSource)
                {
                    try
                    {
                        var linkedTuple = src.GetMangaInfo(newManga.AniListId.Value.ToString());
                        var linked = linkedTuple.Item1;
                        if (!_resolver.TryResolve(primaryCandidate, ToCandidate(linked), out var reason))
                        {
                            _logger.Warn("D-19 validation failed for AniListId={0} on manga {1}: {2}",
                                newManga.AniListId,
                                newManga.Title,
                                reason);
                            newManga.AniListId = null;
                        }
                    }
                    catch (MangaNotFoundException)
                    {
                        _logger.Warn("D-19 validation failed for AniListId={0} on manga {1}: not-found at secondary",
                            newManga.AniListId,
                            newManga.Title);
                        newManga.AniListId = null;
                    }
                }

                // MAL link validation.
                if (newManga.MalId.HasValue && src is MyAnimeListMetadataSource)
                {
                    try
                    {
                        var linkedTuple = src.GetMangaInfo(newManga.MalId.Value.ToString());
                        var linked = linkedTuple.Item1;
                        if (!_resolver.TryResolve(primaryCandidate, ToCandidate(linked), out var reason))
                        {
                            _logger.Warn("D-19 validation failed for MalId={0} on manga {1}: {2}",
                                newManga.MalId,
                                newManga.Title,
                                reason);
                            newManga.MalId = null;
                        }
                    }
                    catch (MangaNotFoundException)
                    {
                        _logger.Warn("D-19 validation failed for MalId={0} on manga {1}: not-found at secondary",
                            newManga.MalId,
                            newManga.Title);
                        newManga.MalId = null;
                    }
                }
            }
        }

        private void ResolveCrossSourceIds(Manga newManga, Manga primaryResult)
        {
            // For each missing cross-source ID, query the corresponding secondary's search-by-title
            // (max 3 search-variant calls per D-22) and gate via CrossSourceIdResolver (D-21).
            var primaryCandidate = ToCandidate(primaryResult);

            // PER D-20 - SYMMETRIC REVERSE DIRECTION (explicit branch):
            // When the primary is AniList or MAL (NOT MangaDex) AND MangaDexId is still null,
            // run fuzzy title match against MangaDex via MangaDexMetadataSource.SearchForNewManga(title)
            // (which calls /manga?title=...) and pick the highest-similarity result clearing the D-21
            // gate. If no match clears the gate, MangaDexId stays null and synthesized chapter-list
            // fallback (D-17 strategy 2) kicks in downstream.
            if (!newManga.MangaDexId.HasValue)
            {
                var primaryDef = _metaFactory.GetPrimary();

                // WR-13 fix: match providers on Implementation (the class name —
                // immutable / not user-editable) instead of Name (user-editable via
                // the developer ProviderControllerBase endpoint). A user who renames
                // MangaDex to "Mangadex 1" used to silently lose the symmetric
                // reverse-direction path.
                if (primaryDef.Implementation != nameof(MangaDexMetadataSource))
                {
                    var mdDef = _metaFactory.All().FirstOrDefault(d => d.Implementation == nameof(MangaDexMetadataSource));
                    if (mdDef != null)
                    {
                        var mdSource = (IMetadataSource)_metaFactory.GetInstance(mdDef);
                        var titles = primaryCandidate.AllTitles.Take(3).Where(t => !string.IsNullOrEmpty(t));
                        foreach (var title in titles)
                        {
                            var hits = mdSource.SearchForNewManga(title);

                            // Pick the highest-similarity hit that clears the D-21 gate.
                            var ranked = hits.Select(h => (Hit: h, Cand: ToCandidate(h)))
                                             .Where(t => _resolver.TryResolve(primaryCandidate, t.Cand, out _))
                                             .ToList();
                            if (ranked.Count > 0)
                            {
                                // First passing hit per D-20 (SearchForNewManga returns ordered by relevance).
                                newManga.MangaDexId = ranked[0].Hit.MangaDexId;
                                _logger.Trace("D-20 reverse-MangaDex resolved MangaDexId={0} for primary={1}",
                                    newManga.MangaDexId,
                                    primaryDef.Name);
                                break;
                            }
                        }
                    }
                }
            }

            // Generic fallback for the OTHER direction(s) - fill remaining missing IDs by querying each
            // non-primary secondary. WR-12 fix: skip MangaDex here when the explicit
            // reverse-MangaDex symmetric branch above already ran (primary != MangaDex).
            // The reverse branch already issued up to 3 search-variant calls against
            // MangaDex; re-iterating would burn another 3 against the 40 req/min budget
            // for no incremental gain. The Implementation type-name (not user-editable
            // Name — see WR-13) is what we filter on.
            var primaryDefForFilter = _metaFactory.GetPrimary();
            var primaryIsMangaDex = primaryDefForFilter.Implementation == nameof(MangaDexMetadataSource);
            var mangaDexImpl = nameof(MangaDexMetadataSource);

            var secondaries = new List<(string Name, IMetadataSource Source)>();
            foreach (var def in _metaFactory.All().Where(d => !d.IsPrimary))
            {
                if (!primaryIsMangaDex && def.Implementation == mangaDexImpl)
                {
                    // Already exercised in the reverse-MangaDex branch above.
                    continue;
                }

                secondaries.Add((def.Name, (IMetadataSource)_metaFactory.GetInstance(def)));
            }

            foreach (var (_, src) in secondaries)
            {
                if (newManga.MangaDexId.HasValue && newManga.MalId.HasValue && newManga.AniListId.HasValue)
                {
                    break;   // all 3 IDs filled
                }

                // Try up to 3 search-variant calls per D-22 (English, romaji, native - best-effort).
                var titles = primaryCandidate.AllTitles.Take(3).Where(t => !string.IsNullOrEmpty(t));
                foreach (var title in titles)
                {
                    var hits = src.SearchForNewManga(title);
                    Manga match = null;
                    foreach (var h in hits)
                    {
                        if (_resolver.TryResolve(primaryCandidate, ToCandidate(h), out _))
                        {
                            match = h;
                            break;
                        }
                    }

                    if (match != null)
                    {
                        newManga.MangaDexId ??= match.MangaDexId;
                        newManga.MalId ??= match.MalId;
                        newManga.AniListId ??= match.AniListId;
                        break;
                    }
                }
            }
        }

        private static MangaCandidate ToCandidate(Manga m) => new MangaCandidate
        {
            AllTitles = m?.Title != null ? new List<string> { m.Title } : new List<string>(),
            PublicationYear = m?.PublicationYear,
            PrimaryAuthor = m?.PrimaryAuthor,
            TotalChapterCount = m?.TotalChapterCount,
        };
    }
}
