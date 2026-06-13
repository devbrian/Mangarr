using System.Collections.Generic;
using NLog;
using NzbDrone.Core.Download.History.Manga;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Parser.Manga.Model;

namespace NzbDrone.Core.Download.TrackedDownloads
{
    // Sonarr divergence: NEW manga sibling per Phase 36 (LOOP-02) — see DIVERGENCE.md.
    // Provenance (control-flow / matcher ordering — there is NO live TV TrackedDownloadService at
    // HEAD, deleted in the Phase 15 Tv/ cutover): v5-develop:src/NzbDrone.Core/Download/
    // TrackedDownloads/TrackedDownloadService.cs (TrackDownload(DownloadClientDefinition,
    // DownloadClientItem) — builds a TrackedDownload from the DownloadId history join + a parse).
    // Role-match analogs (surface conventions): Queue/Manga/MangaQueueService.MapQueueItems
    // (RemoteChapter → manga-shape projection); History/Manga/ChapterHistoryRepository.FindByDownloadId
    // (the DownloadId lookup shape). HIGHEST-RISK FILE IN THE MILESTONE — the publisher it feeds has
    // NO live reference to diff against (Pitfall 1); contract tests, not implementation tests.
    //
    // MATCHER ORDER (Pattern 2 — load-bearing):
    //   (1) PRIMARY — stable DownloadId join: IMangaDownloadHistoryService.GetLatestGrab(DownloadId).
    //       On a hit, MapFromHistory resolves the manga + ALL ChapterIds (Pitfall 2: multi-chapter
    //       packs resolve every id; decimal chapters round-trip through the decimal Chapter model,
    //       never an int path; language variants stay distinct because the grab row — not a
    //       (mangaId, chapterNumber) tuple — is the match key). Robust to title drift.
    //   (2) FALLBACK — title parse: logs a Warn EVERY time it fires (drifting/absent titles become
    //       visible), then IMangaParsingService.GetManga(title) → Map. GetManga may return null →
    //       an ImportBlocked shell TrackedDownload (NEVER throws — anti-pattern D: null/throw
    //       symmetry on the fallback-null path).
    //
    // KEPT slot: populates TrackedDownload.RemoteChapter (TrackedDownload.cs:17) — the canonical
    // manga projection slot. NEVER re-introduce RemoteEpisode (stripped Phase 15). Reuses the kept
    // TrackedDownloadState/Status enums and Fail() (TrackedDownload.cs:43-70) — never redefines them.
    //
    // PAGE-CHANNEL SEAM (D-01 / D-01a / Q-D01 — HARD CONSTRAINT): manga-native page progress
    // (page 7/20) is in-process-specific and ADDITIVE. This service is the cleanest carrier — it
    // exposes GetPageProgress(DownloadId) keyed by the stable DownloadId, sourced from the OPTIONAL
    // injected IMangaDownloadPageProgressSource set (the in-process client supplies one; the gateway
    // path — Phase 38 — supplies none). This service NEVER reads ChapterDownloadState directly and
    // adds ZERO Page* fields to the shared DownloadClientItem POCO (the gateway client implements
    // that contract and reports bytes only). When no source reports pages (gateway path), the lookup
    // returns null and the Queue projection (Plan 06) falls back to bytes/% gracefully (D-01b).
    public interface IMangaTrackedDownloadService
    {
        TrackedDownload TrackDownload(DownloadClientDefinition definition, DownloadClientItem item);

        // Page-progress lookup keyed by the stable DownloadId (D-01 / Q-D01). Returns null on the
        // gateway path (no IMangaDownloadPageProgressSource reports the id) so the Queue caption
        // falls back to bytes/%. The Plan 06 MangaQueueService projection reads this.
        MangaDownloadPageProgress GetPageProgress(string downloadId);
    }

    public class MangaTrackedDownloadService : IMangaTrackedDownloadService
    {
        private readonly IMangaDownloadHistoryService _downloadHistoryService;
        private readonly IMangaParsingService _parsingService;
        private readonly IMangaService _mangaService;
        private readonly IChapterService _chapterService;
        private readonly IEnumerable<IMangaDownloadPageProgressSource> _pageProgressSources;
        private readonly Logger _logger;

        public MangaTrackedDownloadService(
            IMangaDownloadHistoryService downloadHistoryService,
            IMangaParsingService parsingService,
            IMangaService mangaService,
            IChapterService chapterService,
            IEnumerable<IMangaDownloadPageProgressSource> pageProgressSources,
            Logger logger)
        {
            _downloadHistoryService = downloadHistoryService;
            _parsingService = parsingService;
            _mangaService = mangaService;
            _chapterService = chapterService;
            _pageProgressSources = pageProgressSources;
            _logger = logger;
        }

        public TrackedDownload TrackDownload(DownloadClientDefinition definition, DownloadClientItem item)
        {
            // (1) PRIMARY — stable DownloadId join (LOOP-02). Load-bearing; called BEFORE any
            // title-parse so a drifting/empty title cannot defeat a known grab.
            var grabbed = _downloadHistoryService.GetLatestGrab(item.DownloadId);
            if (grabbed != null)
            {
                var remoteChapter = MapFromHistory(grabbed, item);
                return BuildTrackedDownload(definition, item, remoteChapter);
            }

            // (2) FALLBACK — title parse. Logs a Warn EVERY time it fires so drifting titles and
            // grabs predating Migration 009 (no join row) are visible (designed degradation,
            // Pitfall 2). IMangaParsingService.GetManga internally runs MangaParser.ParseChapterTitle.
            _logger.Warn("No download-history row for {0}; falling back to title parse", item.DownloadId);

            var fallbackRemote = MapFromTitle(item);
            return BuildTrackedDownload(definition, item, fallbackRemote);
        }

        public MangaDownloadPageProgress GetPageProgress(string downloadId)
        {
            if (string.IsNullOrWhiteSpace(downloadId) || _pageProgressSources == null)
            {
                return null;
            }

            // First source that knows this DownloadId wins. On the gateway path no source reports
            // the id, so this returns null and the Queue caption falls back to bytes/% (D-01a/D-01b).
            foreach (var source in _pageProgressSources)
            {
                var progress = source.GetPageProgress(downloadId);
                if (progress != null)
                {
                    return progress;
                }
            }

            return null;
        }

        // PRIMARY-path resolution: re-source manga + ALL chapters from the lean DownloadId join.
        // Pitfall 2 divergences (each handled by construction):
        //   * multi-chapter packs — GetChapters(grabbed.ChapterIds) resolves EVERY id, never one.
        //   * decimal chapters — chapters resolve by Id through the decimal Chapter model; no int
        //     round-trip is ever introduced here.
        //   * language variants — the match key is the DownloadId-keyed grab row, NOT a
        //     (mangaId, chapterNumber) tuple, so es and es-la grabs stay distinct.
        private RemoteChapter MapFromHistory(MangaDownloadHistory grabbed, DownloadClientItem item)
        {
            var manga = _mangaService.GetManga(grabbed.MangaId);

            var chapters = grabbed.ChapterIds != null && grabbed.ChapterIds.Count > 0
                ? _chapterService.GetChapters(grabbed.ChapterIds)
                : new List<Chapter>();

            // CR-d: a history HIT whose manga or chapters no longer resolve (manga deleted, or a
            // chapter deleted so only a subset/none of the grabbed ChapterIds resolve) is an
            // UNRESOLVABLE download. Return null so BuildTrackedDownload yields an ImportBlocked
            // shell — NEVER a Downloading row leaking partial metadata into queue/import consumers,
            // and NEVER a silent title-parse fallback (that would mask the data problem). A history
            // MISS still uses title-parse; only a HIT-but-unresolved is blocked here.
            if (manga == null || chapters.Count == 0 || chapters.Count != grabbed.ChapterIds.Count)
            {
                _logger.Warn(
                    "Download-history HIT for {0} could not be fully resolved (manga {1}, {2}/{3} chapters); marking ImportBlocked",
                    item.DownloadId,
                    manga?.Id.ToString() ?? "<deleted>",
                    chapters.Count,
                    grabbed.ChapterIds?.Count ?? 0);
                return null;
            }

            // Re-source ALL grabbed provenance off the join row's Data dictionary. The keys are
            // camelCase because MangaDownloadHistoryService writes them camelCase to match the form the
            // EmbeddedDocumentConverter persists (CR-01). Indexer was being dropped before because the
            // writer wrote "Indexer" but this read "indexer"; ScanlationGroup/TranslatedLanguage were
            // never carried at all, so the import core (MangaCompletedDownloadService) built a
            // LocalChapter with BLANK language/group on the load-bearing primary path. Carrying them
            // here restores the file-header "NON-BLANK provenance" contract.
            //
            // GH-362: guid is now carried too so the failure-path blocklist row
            // (MangaBlocklistService.Handle stores ReleaseGuid = release?.Guid) gets the REAL gateway
            // guid instead of null. With the guid present, blocklist matching tightens to
            // (Title, SourceKey, Guid) and no longer over-blocks a same-titled federated mirror that
            // differs only by guid. A legacy grab row with no "guid" key reads back as null (ReadData
            // empty->null normalization) and the #361 null-tolerant fallback still bounds the loop.
            return new RemoteChapter
            {
                Manga = manga,
                Chapters = chapters,
                Release = new NzbDrone.Core.Parser.Model.ReleaseInfo
                {
                    Title = grabbed.SourceTitle ?? item.Title,
                    Indexer = ReadData(grabbed, "indexer"),
                    ScanlationGroup = ReadData(grabbed, "scanlationGroup"),
                    TranslatedLanguage = ReadData(grabbed, "translatedLanguage"),
                    Guid = ReadData(grabbed, "guid")
                }
            };
        }

        // Reads a provenance value off the grab row's Data dictionary, normalizing the empty-string
        // sentinel (the writer stores string.Empty rather than null) back to null so blank provenance
        // does not masquerade as a populated value downstream.
        private static string ReadData(MangaDownloadHistory grabbed, string key)
        {
            if (grabbed.Data != null && grabbed.Data.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
            {
                return value;
            }

            return null;
        }

        // FALLBACK-path resolution. GetManga returns null when no manga matches the title; in that
        // case the caller builds an ImportBlocked shell (does NOT throw — anti-pattern D symmetry).
        private RemoteChapter MapFromTitle(DownloadClientItem item)
        {
            var manga = _parsingService.GetManga(item.Title);
            if (manga == null)
            {
                return null;
            }

            var parsed = MangaParser.ParseChapterTitle(item.Title);
            var existing = _chapterService.GetChaptersByManga(manga.Id);
            return _parsingService.Map(parsed, manga, existing);
        }

        // Builds the TrackedDownload around the KEPT RemoteChapter slot. A null remoteChapter (the
        // fallback-null path) yields an ImportBlocked shell so the queue still shows the in-flight
        // entry rather than dropping it (mirrors MangaQueueService.MapQueueItems shell-row posture).
        private TrackedDownload BuildTrackedDownload(DownloadClientDefinition definition, DownloadClientItem item, RemoteChapter remoteChapter)
        {
            var trackedDownload = new TrackedDownload
            {
                DownloadClient = definition.Id,
                DownloadItem = item,
                Protocol = definition.Protocol,
                Indexer = remoteChapter?.Release?.Indexer,
                IsTrackable = true,
                State = remoteChapter == null
                    ? TrackedDownloadState.ImportBlocked
                    : TrackedDownloadState.Downloading,
                RemoteChapter = remoteChapter
            };

            if (remoteChapter == null)
            {
                // Surface why the entry is blocked without throwing (anti-pattern D). The title-parse
                // fallback already logged the Warn that led here.
                trackedDownload.Warn("Unable to resolve manga/chapters for download {0}", item.DownloadId);
            }

            return trackedDownload;
        }
    }
}
