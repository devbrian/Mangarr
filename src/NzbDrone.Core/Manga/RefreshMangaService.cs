using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Manga.Commands;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Metadata;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.MetadataSource.MangaBaka;
using NzbDrone.Core.MetadataSource.MangaDex;
using NzbDrone.Core.MetadataSource.MyAnimeList;

namespace NzbDrone.Core.Manga
{
    /// <summary>
    /// META-04 RefreshMangaCommand executor. Per D-22 we process IDs sequentially to keep
    /// the per-source rate-limit budget pressure low. The active primary is resolved
    /// dynamically via <see cref="IMetadataSourceFactory.GetPrimary"/> per D-15 — promoting
    /// a different provider via <c>SetPrimary</c> redirects subsequent refreshes without
    /// editing this class.
    /// </summary>
    public class RefreshMangaService : IExecute<RefreshMangaCommand>
    {
        private readonly IMetadataSourceFactory _metaFactory;
        private readonly IMangaService _mangaService;
        private readonly IChapterService _chapterService;
        private readonly IChapterListService _chapterListService;
        private readonly IShouldRefreshManga _shouldRefreshManga;
        private readonly IMangaDiskScanService _diskScanService;
        private readonly IConfigService _configService;
        private readonly CrossSourceIdResolver _resolver;
        private readonly IMetadataFactory _metadataFactory;
        private readonly IEventAggregator _eventAggregator;
        private readonly ICommandResultReporter _commandResultReporter;
        private readonly Logger _logger;

        public RefreshMangaService(IMetadataSourceFactory metaFactory,
                                   IMangaService mangaService,
                                   IChapterService chapterService,
                                   IChapterListService chapterListService,
                                   IShouldRefreshManga shouldRefreshManga,
                                   IMangaDiskScanService diskScanService,
                                   IConfigService configService,
                                   CrossSourceIdResolver resolver,
                                   IMetadataFactory metadataFactory,
                                   IEventAggregator eventAggregator,
                                   ICommandResultReporter commandResultReporter,
                                   Logger logger)
        {
            _metaFactory = metaFactory;
            _mangaService = mangaService;
            _chapterService = chapterService;
            _chapterListService = chapterListService;
            _shouldRefreshManga = shouldRefreshManga;
            _diskScanService = diskScanService;
            _configService = configService;
            _resolver = resolver;
            _metadataFactory = metadataFactory;
            _eventAggregator = eventAggregator;
            _commandResultReporter = commandResultReporter;
            _logger = logger;
        }

        // quick-260701-e71 — after a successful refresh, invoke every ENABLED IMetadata
        // provider's on-disk series-level WriteMangaMetadata(manga). Stax (the only current
        // consumer) writes stax.json = { mangabakaId } into the manga folder; disabled
        // providers are excluded by IMetadataFactory.Enabled() and CBZ-internal providers
        // (ComicInfo) leave WriteMangaMetadata a no-op (MetadataBase default). A
        // metadata-writer failure can NEVER abort the surrounding refresh batch (WR-07 batch
        // tolerance) — StaxMetadata already self-guards its own disk write internally per
        // D-02/D-03, and the enumeration is defended here too.
        private void WriteMangaMetadataFiles(Manga manga)
        {
            List<IMetadata> providers;

            try
            {
                providers = _metadataFactory.Enabled();
            }
            catch (Exception e)
            {
                _logger.Warn(e, "Couldn't enumerate enabled metadata providers for manga {0}", manga.Title);
                return;
            }

            // CodeRabbit #406: the try/catch is PER PROVIDER so one writer's failure cannot
            // skip the remaining enabled providers for this manga — honoring the documented
            // "each provider is individually defended" invariant. Latent with a single provider
            // today (Stax) but correct as more series-level writers ship.
            foreach (var provider in providers)
            {
                try
                {
                    provider.WriteMangaMetadata(manga);
                }
                catch (Exception e)
                {
                    _logger.Warn(e, "Couldn't write series-level metadata for manga {0} via {1}", manga.Title, provider.Definition?.Name ?? provider.GetType().Name);
                }
            }
        }

        // The cross-source ID matching the active primary. Mirrors the add-time switch
        // in AddMangaService.ResolveSourceIdForPrimary. Returns null when the manga has
        // no id for this primary yet (the auto-relink trigger).
        private static string GetPrimarySourceId(Manga manga, IProvideMangaInfo primary) => primary switch
        {
            MangaBakaMetadataSource _ => manga.MangaBakaId?.ToString(),
            MangaDexMetadataSource _ => manga.MangaDexId?.ToString(),
            AniListMetadataSource _ => manga.AniListId?.ToString(),
            MyAnimeListMetadataSource _ => manga.MalId?.ToString(),
            _ => null,
        };

        // Inverse of GetPrimarySourceId: OVERWRITE the active primary's id field on `target`
        // with the value carried by `source` (a confirmed relink hit). Used only by the
        // repoint-on-404 relink path — there the stale primary id is NON-null, so the fill-null
        // CarryOverCrossSourceIds cannot correct it. Only the primary axis is touched; the other
        // cross-source ids stay under the fill-null (never-clobber) carry-over.
        private static void SetPrimarySourceId(Manga target, IProvideMangaInfo primary, Manga source)
        {
            switch (primary)
            {
                case MangaBakaMetadataSource _:
                    target.MangaBakaId = source.MangaBakaId;
                    break;
                case MangaDexMetadataSource _:
                    target.MangaDexId = source.MangaDexId;
                    break;
                case AniListMetadataSource _:
                    target.AniListId = source.AniListId;
                    break;
                case MyAnimeListMetadataSource _:
                    target.MalId = source.MalId;
                    break;
            }
        }

        private static MangaCandidate ToCandidate(Manga m)
        {
            // Feed the canonical Title plus any AlternativeTitles into the candidate so the
            // relink's title search + CrossSourceIdResolver gate get more variants to match
            // against (the caller's Take(3) already anticipates multiple titles). A manga
            // added under one source whose canonical title differs from the new primary's
            // can still match via an alternate. Title goes first so it remains the best
            // search term; nulls/dupes are stripped.
            var titles = new List<string>();
            if (!string.IsNullOrEmpty(m?.Title))
            {
                titles.Add(m.Title);
            }

            if (m?.AlternativeTitles != null)
            {
                titles.AddRange(m.AlternativeTitles);
            }

            return new MangaCandidate
            {
                AllTitles = titles.Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList(),
                PublicationYear = m?.PublicationYear,
                PrimaryAuthor = m?.PrimaryAuthor,
                TotalChapterCount = m?.TotalChapterCount,
            };
        }

        // Fill-null carry-over of every cross-source ID from an authoritative source record
        // onto the target. NEVER overwrites an existing id (`??=`), so it can only ENRICH the
        // link set — it cannot repoint a manga at a different source's id (preserving the
        // post-add immutability spirit that Manga.ApplyChanges enforces by omission). Mirrors
        // AddMangaService's add-time carry-over. Called on every refresh against the full
        // GetMangaInfo record (whose `source` block carries all 7 ids — unlike the lighter
        // search-list response), so a manga whose links arrived partial self-heals to the
        // complete set on the next refresh. Returns true when at least one id was filled.
        private static bool CarryOverCrossSourceIds(Manga target, Manga source)
        {
            if (target == null || source == null)
            {
                return false;
            }

            var before = (target.MangaBakaId, target.MangaDexId, target.MalId, target.AniListId,
                target.KitsuId, target.AnimeNewsNetworkId, target.ShikimoriId,
                target.AnimePlanetId, target.MangaUpdatesId);

            target.MangaBakaId ??= source.MangaBakaId;
            target.MangaDexId ??= source.MangaDexId;
            target.MalId ??= source.MalId;
            target.AniListId ??= source.AniListId;
            target.KitsuId ??= source.KitsuId;
            target.AnimeNewsNetworkId ??= source.AnimeNewsNetworkId;
            target.ShikimoriId ??= source.ShikimoriId;
            target.AnimePlanetId ??= source.AnimePlanetId;
            target.MangaUpdatesId ??= source.MangaUpdatesId;

            return before != (target.MangaBakaId, target.MangaDexId, target.MalId, target.AniListId,
                target.KitsuId, target.AnimeNewsNetworkId, target.ShikimoriId,
                target.AnimePlanetId, target.MangaUpdatesId);
        }

        // Authoritative-record signal: how many cross-source ids + author credits a search
        // hit carries. A metadata source routinely exposes BOTH a canonical record — real
        // author credits + AniList/MAL/MangaUpdates ids — AND sparse duplicate stubs that
        // carry only the primary's own id and an exact canonical-title string. When several
        // candidates clear the resolver gate, the richer record is the canonical one; this
        // is the tie-breaker that keeps a bare stub from being relinked over it. The primary
        // source's own id (e.g. MangaBakaId for a MangaBaka hit) is deliberately NOT counted
        // — it is present on every hit from that primary, so it cannot discriminate.
        private static int CrossSourceIdRichness(Manga m)
        {
            if (m == null)
            {
                return 0;
            }

            var count = 0;
            if (m.MangaDexId.HasValue)
            {
                count++;
            }

            if (m.MalId.HasValue)
            {
                count++;
            }

            if (m.AniListId.HasValue)
            {
                count++;
            }

            if (m.KitsuId.HasValue)
            {
                count++;
            }

            if (m.AnimeNewsNetworkId.HasValue)
            {
                count++;
            }

            if (m.ShikimoriId.HasValue)
            {
                count++;
            }

            if (!string.IsNullOrEmpty(m.AnimePlanetId))
            {
                count++;
            }

            if (!string.IsNullOrEmpty(m.MangaUpdatesId))
            {
                count++;
            }

            if (!string.IsNullOrEmpty(m.PrimaryAuthor))
            {
                count++;
            }

            return count;
        }

        // Auto-relink a manga that has no cross-source ID for the now-active primary.
        // Searches the active primary by the manga's title(s) and confirms the match via
        // CrossSourceIdResolver (Jaro-Winkler >= 0.85 title + 2-of-3 axis confirm on
        // year/author/chapter-count) — the same gate AddMangaService uses at add-time
        // (AddMangaService.ResolveCrossSourceIds, D-20/D-21).
        //
        // Selection: pick the BEST confident match across EVERY searched title — NOT merely
        // the first gate-passer in provider order. A source can return several records for
        // one series: the authoritative record (real author credits + cross-source ids)
        // alongside sparse duplicate stubs whose only virtue is an exact canonical-title
        // string. First-passing selection let a stub win purely on search order — e.g.
        // MangaBaka "The Forgotten Field" returned the canonical record 586650 AND a bare
        // 574752 stub, and the stub was chosen. Candidates are ranked by
        // (cross-source-id richness, confirmed-axis count, title similarity) so the canonical
        // record wins whenever it also clears the gate.
        //
        // On a confident match the matched record's cross-source ids are carried onto the
        // existing row (fill-null, never clobber) and persisted; returns the resolved primary
        // source id. Returns null when the primary cannot search, every search errors, or no
        // hit clears the gate (caller skip-warns and leaves the existing metadata untouched).
        private string TryRelinkPrimaryId(Manga existing, IProvideMangaInfo primary, MetadataSourceDefinition primaryDef, bool repointStalePrimaryId = false)
        {
            if (primary is not ISearchForNewManga searcher)
            {
                return null;
            }

            var candidate = ToCandidate(existing);

            Manga best = null;
            (int IdRichness, int Axes, double Similarity) bestKey = default;

            foreach (var title in candidate.AllTitles.Take(3).Where(t => !string.IsNullOrEmpty(t)))
            {
                List<Manga> hits;
                try
                {
                    hits = searcher.SearchForNewManga(title);
                }
                catch (Exception e)
                {
                    // One failed search must not abort the surrounding refresh batch.
                    _logger.Debug(e, "Auto-relink search failed for manga {0} against {1}", existing.Title, primaryDef.Name);
                    continue;
                }

                if (hits == null)
                {
                    continue;
                }

                foreach (var hit in hits)
                {
                    // A relink target must BOTH expose an id for THIS primary AND clear the gate.
                    if (string.IsNullOrEmpty(GetPrimarySourceId(hit, primary)))
                    {
                        continue;
                    }

                    var score = _resolver.Score(candidate, ToCandidate(hit));
                    if (!score.Passed)
                    {
                        continue;
                    }

                    var key = (CrossSourceIdRichness(hit), score.ConfirmedAxes, score.TitleSimilarity);
                    if (best == null || key.CompareTo(bestKey) > 0)
                    {
                        best = hit;
                        bestKey = key;
                    }
                }
            }

            if (best == null)
            {
                return null;
            }

            // When the stored primary id 404'd (repointStalePrimaryId), the fill-null carry-over
            // below CANNOT replace it — the stale id is non-null. Directly repoint the primary id
            // at the confirmed hit's id first; the OTHER cross-source ids still go through the
            // fill-null carry-over (only the dead primary axis is corrected). The hit was
            // pre-filtered above to expose a non-empty primary id, so this always yields a usable
            // resolvedId below. (Debug session manga-removed-from-metadata-source, 2026-06-18.)
            if (repointStalePrimaryId)
            {
                SetPrimarySourceId(existing, primary, best);
            }

            // Carry over whatever cross-source ids the chosen hit exposes (the search-list
            // response is lighter than the full record and may omit some). The full set is
            // enriched right after, when the refresh fetches GetMangaInfo for the resolved id.
            CarryOverCrossSourceIds(existing, best);

            var resolvedId = GetPrimarySourceId(existing, primary);
            if (string.IsNullOrEmpty(resolvedId))
            {
                // Defensive: the chosen hit was pre-filtered to carry the primary id, so this
                // is unreachable in practice. Signal no-resolution rather than relink to null.
                return null;
            }

            // Persist the relink. publishUpdatedEvent:false mirrors the metadata-write
            // suppression the rest of the refresh path uses (UI repaint fires via the
            // trailing ChapterListUpdatedEvent once chapters sync).
            _mangaService.UpdateManga(existing, publishUpdatedEvent: false);
            _logger.Info("Auto-relinked manga {0} to active primary {1} (source id {2}) by best confirmed title match (axes={3}, cross-source-links={4})",
                existing.Title,
                primaryDef.Name,
                resolvedId,
                bestKey.Axes,
                bestKey.IdRichness);

            return resolvedId;
        }

        // gap-12 (refresh-also-scan-disk): mirrors Tv/RefreshSeriesService.RescanSeries
        // (deleted in commit 7794a184c, but historically the canonical Sonarr pattern).
        // After metadata refresh succeeds, the same Refresh trigger ALSO walks the manga's
        // root folder and imports any orphan files into the DB — single user click, both
        // sides reconciled. Gating mirrors TV verbatim:
        //   * isNew → force-rescan regardless of config (post-add lifecycle expects scan).
        //   * RescanAfterRefreshType.Never → skip; publish MangaScanSkippedEvent.
        //   * RescanAfterRefreshType.AfterManual + scheduled trigger → skip; publish event.
        //   * Otherwise (Always, OR AfterManual+Manual) → invoke disk scan.
        // Catch-all around _diskScanService.Scan because one bad disk-scan must NOT abort
        // the surrounding manga refresh batch (WR-07-style invariant).
        private void RescanManga(Manga manga, bool isNew, CommandTrigger trigger)
        {
            var rescanAfterRefresh = _configService.RescanAfterRefresh;

            if (isNew)
            {
                _logger.Trace("Forcing rescan of {0}. Reason: New manga", manga);
            }
            else if (rescanAfterRefresh == RescanAfterRefreshType.Never)
            {
                _logger.Trace("Skipping rescan of {0}. Reason: never rescan after refresh", manga);
                _eventAggregator.PublishEvent(new MangaScanSkippedEvent(manga, MangaScanSkippedReason.NeverRescanAfterRefresh));

                return;
            }
            else if (rescanAfterRefresh == RescanAfterRefreshType.AfterManual && trigger != CommandTrigger.Manual)
            {
                _logger.Trace("Skipping rescan of {0}. Reason: not after automatic scans", manga);
                _eventAggregator.PublishEvent(new MangaScanSkippedEvent(manga, MangaScanSkippedReason.RescanAfterManualRefreshOnly));

                return;
            }

            try
            {
                _diskScanService.Scan(manga);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Couldn't rescan manga {0}", manga);
            }
        }

        // Fetch + apply the authoritative record for ONE manga from the active primary, then
        // sync chapters, publish the refresh events, and rescan disk. Extracted verbatim from
        // Execute's per-manga loop so the MangaNotFoundException relink-on-404 path can RETRY it
        // against a freshly-resolved primary id (debug session
        // manga-removed-from-metadata-source, 2026-06-18). A MangaNotFoundException thrown by
        // GetMangaInfo propagates straight through to the caller's catch — that catch owns the
        // relink-then-removed-at-source decision.
        private void RefreshMangaInfo(Manga existing, string sourceId, IProvideMangaInfo primary, RefreshMangaCommand message)
        {
            var tuple = primary.GetMangaInfo(sourceId);
            var mangaInfo = tuple.Item1;

            // Phase 16.1 Wave 3 (REVERT-03): chapter feed is the Sonarr-canonical
            // IEnumerable<Chapter> shape. Materialize once for the snapshot/diff
            // delta computation below + the SyncChapters call.
            var remoteChapters = tuple.Item2.ToList();

            // Manga.ApplyChanges copies user-mutable fields (Monitored,
            // RootFolderPath, Tags, AddOptions, MonitorNewItems,
            // TranslationProfileId, CustomFormatProfileId) per gap-02; on a
            // metadata-fetch path mangaInfo carries default values for those
            // fields (the source doesn't know the user's choice), so without
            // preserving the user's values across the call we silently flip them
            // back to defaults on every refresh — and MangaEditedService queues
            // a refresh after every UI single-edit, so without this preservation
            // every Save round-trip clobbers itself. AddMangaService.PrepareForAdd
            // uses the same dance at lines 236-248. Phase 9 should restructure
            // ApplyChanges to drop the user-field copy entirely (TV's
            // Series.ApplyChanges-from-metadata path doesn't have this problem
            // because RefreshSeriesService doesn't call ApplyChanges — it manually
            // copies metadata-only fields).
            var userMonitored = existing.Monitored;
            var userRootFolderPath = existing.RootFolderPath;
            var userTags = existing.Tags;
            var userAddOptions = existing.AddOptions;
            var userMonitorNewItems = existing.MonitorNewItems;
            var userTranslationProfileId = existing.TranslationProfileId;
            var userCustomFormatProfileId = existing.CustomFormatProfileId;

            // Debug session add-manga-lookup-null-path (2026-05-12): Path MUST
            // be saved/restored across ApplyChanges. Manga.cs:135 (issue #81
            // bug-fix) explicitly copies Path = other.Path inside ApplyChanges
            // so MoveMangaCommand can land the new on-disk path through the
            // V5 PUT controller. But on the metadata-refresh path here, mangaInfo
            // (returned by MangaDexMetadataSource) NEVER sets Path — metadata
            // sources don't know disk paths. Without saving existing.Path across
            // ApplyChanges, line 229's `new DirectoryInfo(existing.Path).FullName`
            // throws ArgumentNullException AND the trailing UpdateManga call
            // writes Path=null which trips the SQLite NOT NULL constraint
            // (001_mangarr_baseline.cs:510). Mirrors the same dance in
            // AddMangaService.PrepareForAdd (lines 251 + 262).
            var userPath = existing.Path;

            existing.ApplyChanges(mangaInfo);

            existing.Monitored = userMonitored;
            existing.RootFolderPath = userRootFolderPath ?? existing.RootFolderPath;
            existing.Tags = userTags ?? existing.Tags;
            existing.AddOptions = userAddOptions ?? existing.AddOptions;
            existing.MonitorNewItems = userMonitorNewItems;
            existing.TranslationProfileId = userTranslationProfileId;
            existing.CustomFormatProfileId = userCustomFormatProfileId;
            existing.Path = userPath ?? existing.Path;

            // Enrich cross-source links from the authoritative full record. Manga.ApplyChanges
            // deliberately OMITS the cross-source ids (they are immutable post-add), but the
            // GetMangaInfo record's `source` block carries the complete set (e.g. MangaBaka's
            // 7 ids) — richer than the search-list response the relink path may have used. Fill
            // only the nulls, so a manga added under one source self-heals to the full link set
            // on refresh under another primary, while never repointing an already-set id.
            CarryOverCrossSourceIds(existing, mangaInfo);

            // gap-06: mirror RefreshSeriesService.RefreshSeriesInfo
            // (Tv/RefreshSeriesService.cs:116-124) — normalize Manga.Path to
            // its full absolute form with actual disk casing on every refresh.
            // Defends against OS-level renames (Windows casing drift) so the
            // file-import pipeline can still match canonical paths.
            try
            {
                // Debug session add-manga-lookup-null-path (2026-05-12) defense-in-depth:
                // even with the userPath save/restore above, legacy/test rows could
                // carry an empty Path (pre-Phase-15 baseline). Skip normalization
                // rather than throw + swallow.
                if (string.IsNullOrWhiteSpace(existing.Path))
                {
                    _logger.Warn("Skipping path normalization for manga {0} (Id={1}): Path is null/empty",
                        existing.Title,
                        existing.Id);
                }
                else
                {
                    existing.Path = new DirectoryInfo(existing.Path).FullName;
                    existing.Path = existing.Path.GetActualCasing();
                }
            }
            catch (Exception e)
            {
                _logger.Warn(e, "Couldn't update manga path for " + existing.Path);
            }

            // gap-11: suppress UpdateManga's event publish so the trailing
            // PublishEvent below is the SOLE MangaUpdatedEvent per refresh,
            // emitted AFTER chapter sync completes. Mirrors TV
            // RefreshSeriesService.RefreshSeriesInfo's UpdateSeries(publishUpdatedEvent:false)
            // → RefreshEpisodeInfo → PublishEvent(SeriesUpdatedEvent) ordering
            // (Pitfall 4 invariant: DB write FIRST, event LAST).
            _mangaService.UpdateManga(existing, publishUpdatedEvent: false);

            // Phase 8 backfill (audit gap: no-sibling/EpisodeRefreshedService.md +
            // RefreshSeriesService-vs-RefreshMangaService.md gap-10 reclassified):
            // snapshot the chapter set BEFORE the chapter-sync pass so we can compute
            // the (added/updated/removed) delta for ChapterInfoRefreshedEvent. Mirrors
            // RefreshEpisodeService.RefreshEpisodeInfo (Tv/RefreshEpisodeService.cs:131)
            // which publishes EpisodeInfoRefreshedEvent with the equivalent delta.
            //
            // The chapter-sync pass does not return a delta (its public surface predates
            // this requirement), so we snapshot+diff here. The diff key is ChapterId —
            // rows whose ID exists in both snapshots count as "updated" (SyncChapters
            // may have updated mutable fields in place); IDs only present post-sync are
            // "added"; IDs only present pre-sync are "removed". Per Phase 16.1 Wave 3
            // locked stale-handling decision SyncChapters does NOT delete stale Chapter
            // rows, so removed will be empty unless a separate deletion path runs.
            var beforeIds = _chapterService.GetChaptersByManga(existing.Id)
                .ToDictionary(c => c.Id);

            // Phase 16.1 Wave 3 (REVERT-03): single SyncChapters call per refresh.
            // Mirror of Sonarr's RefreshEpisodeService.RefreshEpisodeInfo.
            _chapterListService.SyncChapters(existing, remoteChapters);

            var afterChapters = _chapterService.GetChaptersByManga(existing.Id);
            var added = afterChapters.Where(c => !beforeIds.ContainsKey(c.Id)).ToList();
            var updated = afterChapters.Where(c => beforeIds.ContainsKey(c.Id)).ToList();
            var removed = beforeIds.Values.Where(c => afterChapters.All(a => a.Id != c.Id)).ToList();

            _eventAggregator.PublishEvent(new ChapterInfoRefreshedEvent(existing, added, updated, removed));

            // Pitfall 4: SINGLE ChapterListUpdatedEvent emit AFTER both passes complete.
            // Existing consumer contract preserved (one event per refresh; SignalR
            // fan-out unchanged).
            _eventAggregator.PublishEvent(new ChapterListUpdatedEvent(existing));

            _eventAggregator.PublishEvent(new MangaUpdatedEvent(existing));

            // gap-12 (refresh-also-scan-disk): synchronously rescan the manga's
            // root folder so manually-placed CBZ/CBR files get reconciled into
            // the DB on the SAME user click. Single click does both — mirror of
            // Tv/RefreshSeriesService.Execute calling RescanSeries(...) per id.
            // Placed AFTER the MangaUpdatedEvent so subscribers see the metadata
            // update first, then the file-side update via MangaScannedEvent.
            RescanManga(existing, message.IsNewManga, message.Trigger);

            // quick-260701-e71 — write series-level on-disk metadata (Stax stax.json) for
            // enabled providers. SINGLE call site, placed AFTER RescanManga on the success
            // path so it runs on EVERY successful refresh (new + existing) exactly once. At
            // this point existing.Path is normalized to its absolute/actual-casing form and
            // persisted, and cross-source ids (including MangaBakaId) have been carried over
            // from the authoritative record via CarryOverCrossSourceIds — so MangaBakaId is at
            // its freshest. This covers BOTH add and refresh (MangaAddedHandler funnels adds
            // through RefreshMangaCommand). D-02 self-heal + D-03 skip-when-no-id are handled
            // inside the provider. Deliberately NOT called from the catch/relink paths — a
            // removed-at-source or errored manga should not get a fresh stax write here; the
            // next clean refresh handles it.
            WriteMangaMetadataFiles(existing);
        }

        public void Execute(RefreshMangaCommand message)
        {
            // gap-01: publish the "refresh starting" pulse BEFORE any iteration.
            // Mirrors TV RefreshSeriesService.Execute (Tv/RefreshSeriesService.cs:215)
            // — UI / SignalR subscribers need this to surface a "refreshing" indicator
            // at parity with TV's UX feedback model. Pairs with the trailing
            // MangaRefreshCompleteEvent (gap-02) emitted after the iteration finishes.
            _eventAggregator.PublishEvent(new MangaRefreshStartingEvent(message.Trigger == CommandTrigger.Manual));

            // gap-03 (audit/RefreshSeriesService-vs-RefreshMangaService.md) +
            // no-sibling/ShouldRefreshSeries.md: distinguish the two TV branches that
            // RefreshMangaService consolidated into a single loop. TV gates only the
            // scheduled refresh-all branch (Tv/RefreshSeriesService.cs:251-281); the
            // explicit-IDs branch refreshes unconditionally because the user already
            // narrowed the request.
            var isRefreshAll = message.MangaIds == null || message.MangaIds.Count == 0;
            var ids = isRefreshAll
                ? _mangaService.AllMangaIds()
                : message.MangaIds;

            var primaryDef = _metaFactory.GetPrimary();
            var primary = (IProvideMangaInfo)_metaFactory.GetInstance(primaryDef);

            // gh199 fix: track the ids whose data actually went through the metadata-fetch
            // pipeline so the trailing MangaRefreshCompleteEvent can scope its payload.
            // Populated AFTER all skip-paths (manga missing / scheduled cooldown / no
            // source-id) so AutoTagging re-eval only fires for mangas that could have
            // changed. All three try/catch arms (success / MangaNotFoundException /
            // generic WR-07 catch) leave the manga in a possibly-updated DB state, so
            // any id reaching the try block is eligible for re-tag. Only used on the
            // explicit-IDs branch; the refresh-all branch publishes a null-scope event
            // to preserve the AT-06 retroactive full-library invariant (e.g. a new
            // RootFolder appears mid-scheduled-refresh and unlocks rules for other manga).
            var refreshedIds = new List<int>();

            // PER D-22: sequential per manga to keep concurrent budget pressure low.
            foreach (var id in ids)
            {
                var existing = _mangaService.GetManga(id);
                if (existing == null)
                {
                    continue;
                }

                // gap-03: rate-limit-budget gate. TV mirror: Tv/RefreshSeriesService.cs:256
                // (`if (trigger == CommandTrigger.Manual || _checkIfSeriesShouldBeRefreshed.ShouldRefresh(series))`).
                // The OTHER half of D-22's rate-limit-budget protection — D-22 makes the
                // loop sequential, but without this gate every scheduled tick still hammers
                // MangaDex/AniList/MAL for every manga unconditionally. Manual-trigger
                // bypass mirrors TV: explicit user request overrides the cooldown.
                // Only applies on the scheduled refresh-all branch — explicit-IDs requests
                // are user-narrowed and refresh unconditionally.
                if (isRefreshAll
                    && message.Trigger != CommandTrigger.Manual
                    && !_shouldRefreshManga.ShouldRefresh(existing))
                {
                    _logger.Info("Skipping refresh of manga: {0}", existing.Title);
                    continue;
                }

                // Use the cross-resolved ID matching this primary.
                var sourceId = GetPrimarySourceId(existing, primary);

                if (string.IsNullOrEmpty(sourceId))
                {
                    // The manga was added under a DIFFERENT primary (e.g. MangaDex) and
                    // never received a cross-source ID for the now-active primary (e.g.
                    // MangaBaka after the user promoted it). Before skipping, attempt an
                    // auto-relink: search the active primary by the manga's title and
                    // confirm the match via CrossSourceIdResolver (the same D-20/D-21
                    // resolution AddMangaService runs at add-time). On success the manga
                    // is repointed at the new primary and the refresh proceeds; a whole
                    // library added under MangaDex heals itself on the next refresh.
                    sourceId = TryRelinkPrimaryId(existing, primary, primaryDef);
                }

                if (string.IsNullOrEmpty(sourceId))
                {
                    // WR-08 fix: escalate from Trace to Warn so users notice that a
                    // manga in their library is silently being skipped because the
                    // active primary has no cross-source ID for it AND auto-relink
                    // could not confidently match it. The CONTEXT D-23 manual-relink
                    // endpoint is the remediation path; without a visible signal users
                    // would never know it's needed. (Phase 7+ can extend this to a
                    // MissingPrimarySourceIdHealthCheck per the review's secondary
                    // recommendation.)
                    _logger.Warn("Skipping manga {0}: no source ID for active primary {1} and auto-relink found no confident match; manual relink required (POST /api/v5/manga/{2}/links)",
                        existing.Title,
                        primaryDef.Name,
                        existing.Id);
                    continue;
                }

                // gh199 fix: id has passed all skip-gates and is about to enter the
                // metadata-fetch pipeline. Record BEFORE the try block so all three
                // outcome arms (success / MangaNotFoundException / WR-07 generic catch)
                // are covered.
                refreshedIds.Add(id);

                try
                {
                    RefreshMangaInfo(existing, sourceId, primary, message);
                }
                catch (MangaNotFoundException) when (!message.IsNewManga)
                {
                    // The stored primary-source id 404'd. Before declaring the manga
                    // removed-at-source, give it the SAME confident title-search relink the
                    // empty-id path uses (TryRelinkPrimaryId) — MangaBaka (and similar) periodically
                    // REBUILD their id space, retiring the id a manga was added under (a hard 404)
                    // while the title lives on at a NEW id. repointStalePrimaryId:true lets the
                    // relink OVERWRITE the dead primary id (the default fill-null carry-over cannot,
                    // since the stale id is non-null). On a confident match we retry the fetch
                    // against the resolved id and skip the removed-at-source flip entirely.
                    // Debug session manga-removed-from-metadata-source (2026-06-18).
                    var relinkedId = TryRelinkPrimaryId(existing, primary, primaryDef, repointStalePrimaryId: true);
                    if (!string.IsNullOrEmpty(relinkedId) && relinkedId != sourceId)
                    {
                        try
                        {
                            _logger.Info("Manga {0} primary id {1} not found at {2}; retrying refresh against relinked id {3}",
                                existing.Title,
                                sourceId,
                                primaryDef.Name,
                                relinkedId);
                            RefreshMangaInfo(existing, relinkedId, primary, message);

                            // Healed onto the live id — skip the removed-at-source flip below.
                            continue;
                        }
                        catch (MangaNotFoundException)
                        {
                            // The relinked id ALSO 404'd — fall through to the removed-at-source
                            // flip. A bad-match relink cannot strand the manga: the resolver gate
                            // already cleared the candidate, and the next refresh re-resolves.
                            _logger.Warn("Relinked id {0} for manga {1} also not found at {2}; marking removed-at-source",
                                relinkedId,
                                existing.Title,
                                primaryDef.Name);
                        }
                        catch (Exception retryEx)
                        {
                            // WR-07 batch tolerance: a NON-404 failure on the relinked fetch
                            // (HTTP 503, JSON deser error, chapter-sync exception, …) must NOT
                            // escape this catch and abort the whole refresh loop — and must NOT
                            // flip the manga to deleted (it is not a removal). Mirror the sibling
                            // generic catch below: log, rescan disk, flag Indeterminate, continue.
                            // The repointed primary id is already persisted, so the next refresh
                            // retries against the live id.
                            _logger.Warn(retryEx,
                                "Refresh of relinked id {0} for manga {1} failed; skipping and continuing",
                                relinkedId,
                                existing.Title);
                            RescanManga(existing, message.IsNewManga, message.Trigger);
                            _commandResultReporter.Report(CommandResult.Indeterminate);
                            continue;
                        }
                    }

                    _logger.Warn("Manga {0} not found at primary source — preserving existing data",
                        existing.Title);

                    // gap-07: mirror RefreshSeriesService.RefreshSeriesInfo
                    // (Tv/RefreshSeriesService.cs:70-81) — when the primary source no longer
                    // returns this manga, flip Status to the "deleted" string-sentinel so the
                    // UI can surface "removed at source". String sentinel (not a schema
                    // column) is the audit's stated alternative to a new RemovedAtSource
                    // bool — joins the documented set on Manga.cs:54
                    // (ongoing | completed | hiatus | cancelled | deleted). Persists via
                    // UpdateManga(publishUpdatedEvent:false) so the trailing
                    // PublishEvent(MangaUpdatedEvent) is the SOLE update event for this
                    // iteration (Pitfall 4 invariant: DB write FIRST, event LAST).
                    //
                    // Divergence from TV: do NOT rethrow. Manga refresh is batch-tolerant
                    // per WR-07 — one removed-at-source manga must not abort the whole loop.
                    // TV rethrows because its outer Execute treats single-series refreshes
                    // as fatal; manga's outer Execute is sequential-tolerant by design.
                    if (existing.Status != MangaStatusType.Deleted)
                    {
                        existing.Status = MangaStatusType.Deleted;
                        _mangaService.UpdateManga(existing, publishUpdatedEvent: false);
                        _logger.Debug("Manga marked as deleted at source for {0}", existing.Title);
                        _eventAggregator.PublishEvent(new MangaUpdatedEvent(existing));
                    }

                    // gap-12 (refresh-also-scan-disk): TV verbatim — even when metadata
                    // refresh hits SeriesNotFound, RefreshSeriesService still calls
                    // RescanSeries before the catch returns. Manga-side rescan must run
                    // here too so a deleted-at-source manga can still surface local file
                    // changes (e.g., user moved scans into the folder before discovering
                    // upstream removal). Mirrors Tv/RefreshSeriesService.cs:235 ordering
                    // — RescanSeries runs INSIDE the SeriesNotFoundException catch.
                    RescanManga(existing, message.IsNewManga, message.Trigger);

                    // gap-09: mirror RefreshSeriesService.Execute (Tv/RefreshSeriesService.cs:235)
                    // — flag the command result Indeterminate so a partial-success batch is
                    // not falsely marked Completed by the command queue / health check.
                    _commandResultReporter.Report(CommandResult.Indeterminate);
                }
                catch (Exception ex)
                {
                    // WR-07 fix: catch every non-MangaNotFound failure so one bad
                    // manga (HTTP 503, JSON deser error, MangaDex maintenance, …)
                    // does NOT abort the entire refresh batch. D-22 mandates
                    // sequential refreshes specifically to keep one bad manga from
                    // blowing up the whole loop; without this catch the previous
                    // implementation did the opposite.
                    _logger.Warn(ex, "Refresh failed for manga {0}; skipping and continuing", existing.Title);

                    // gap-12 (refresh-also-scan-disk): TV verbatim — even on the catch-all
                    // failure path, RefreshSeriesService still calls RescanSeries before
                    // continuing the loop. Manga-side mirrors that ordering — a transient
                    // upstream failure (HTTP 503, JSON error) must not block the local
                    // file reconciliation, since the disk-scan is independent of the
                    // metadata fetch and may itself succeed.
                    RescanManga(existing, message.IsNewManga, message.Trigger);

                    // gap-09: mirror RefreshSeriesService.Execute (Tv/RefreshSeriesService.cs:245)
                    // — flag the command result Indeterminate so a partial-success batch is
                    // not falsely marked Completed by the command queue / health check.
                    _commandResultReporter.Report(CommandResult.Indeterminate);
                }
            }

            // gap-02: publish the "refresh complete" pulse AFTER the iteration finishes.
            // Mirrors TV RefreshSeriesService.Execute's trailing
            // PublishEvent(new SeriesRefreshCompleteEvent()) — UI / SignalR subscribers
            // need this to clear the "refreshing" indicator. Pairs with the (gap-01)
            // MangaRefreshStartingEvent emitted at the top of Execute.
            //
            // gh199 fix: pass the scope of actually-refreshed ids on the explicit-IDs
            // branch so subscribers like MangaAutoTaggingApplier scope their iteration
            // accordingly. On the refresh-all branch, publish with null MangaIds to
            // preserve the AT-06 retroactive full-library re-eval invariant (a refresh
            // sweep may have unlocked rules that depend on RootFolderPath or other
            // cross-manga state — only the library-wide re-eval is correct there).
            _eventAggregator.PublishEvent(new MangaRefreshCompleteEvent(isRefreshAll ? null : refreshedIds));
        }
    }
}
