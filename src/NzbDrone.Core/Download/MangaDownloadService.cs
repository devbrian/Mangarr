using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Common.TPL;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Manga.Model;

namespace NzbDrone.Core.Download
{
    // Sonarr divergence: NEW manga sibling per Phase 15 Wave (A) pre-land per
    // .planning/phases/15-domain-rename-rebrand/15-CONTRACTS-AUDIT.md §8.
    // Role-match analog: src/NzbDrone.Core/Download/DownloadService.cs.
    //
    // Differences from the TV DownloadService:
    //   * Consumes RemoteChapter (manga DTO) directly — no ToRemoteEpisodeShim()
    //     bridge needed.
    //   * Publishes ChapterGrabbedEvent (manga sibling) instead of EpisodeGrabbedEvent.
    //   * No ISeedConfigProvider — manga uses Http protocol (no torrent seeding).
    //   * IDownloadClient.Download still takes RemoteEpisode at the v1 surface (TV
    //     contract); MangaDownloadService delegates via the in-process client's new
    //     manga-shape Download(RemoteChapter, IIndexer) overload (Wave (A) W-6).
    //     Until that overload lands, this service falls back to the TV-shape
    //     overload through a local shim — the shim is private to this file (not the
    //     public RemoteChapter.ToRemoteEpisodeShim() static this wave deletes), so
    //     deleting the public shim does not break Wave (A) build green.
    //
    // Phase 15 Wave (C) will delete TV DownloadService and rename this to
    // DownloadService — the duplication is intentional during cutover.
    public class MangaDownloadService : IMangaDownloadService
    {
        private readonly IProvideDownloadClient _downloadClientProvider;
        private readonly IDownloadClientStatusService _downloadClientStatusService;
        private readonly IIndexerFactory _indexerFactory;
        private readonly IIndexerStatusService _indexerStatusService;
        private readonly IRateLimitService _rateLimitService;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public MangaDownloadService(IProvideDownloadClient downloadClientProvider,
                                    IDownloadClientStatusService downloadClientStatusService,
                                    IIndexerFactory indexerFactory,
                                    IIndexerStatusService indexerStatusService,
                                    IRateLimitService rateLimitService,
                                    IEventAggregator eventAggregator,
                                    Logger logger)
        {
            _downloadClientProvider = downloadClientProvider;
            _downloadClientStatusService = downloadClientStatusService;
            _indexerFactory = indexerFactory;
            _indexerStatusService = indexerStatusService;
            _rateLimitService = rateLimitService;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public async Task DownloadReport(RemoteChapter remoteChapter, int? downloadClientId)
        {
            Ensure.That(remoteChapter, () => remoteChapter).IsNotNull();
            Ensure.That(remoteChapter.Manga, () => remoteChapter.Manga).IsNotNull();

            var filterBlockedClients = remoteChapter.Release.PendingReleaseReason == PendingReleaseReason.DownloadClientUnavailable;
            var tags = remoteChapter.Manga?.Tags;

            if (downloadClientId.HasValue)
            {
                var specificClient = _downloadClientProvider.Get(downloadClientId.Value);
                await DownloadReport(remoteChapter, specificClient);

                return;
            }

            var availableClients = _downloadClientProvider.GetDownloadClients(
                remoteChapter.Release.DownloadProtocol,
                remoteChapter.Release.IndexerId,
                filterBlockedClients,
                tags).ToList();

            if (!availableClients.Any())
            {
                throw new DownloadClientUnavailableException($"No {remoteChapter.Release.DownloadProtocol} download client available");
            }

            var triedClients = new HashSet<int>();

            foreach (var downloadClient in availableClients)
            {
                if (triedClients.Contains(downloadClient.Definition.Id))
                {
                    continue;
                }

                try
                {
                    _logger.Debug("Attempting manga download with client: {0}", downloadClient.Definition.Name);
                    await DownloadReport(remoteChapter, downloadClient);

                    _downloadClientProvider.ReportSuccessfulDownloadClient(
                        remoteChapter.Release.DownloadProtocol,
                        downloadClient.Definition.Id);

                    return;
                }
                catch (DownloadClientException ex)
                {
                    _logger.Trace(ex, "Unable to add manga report to download client: {0}", downloadClient.Definition.Name);
                    triedClients.Add(downloadClient.Definition.Id);
                }
                catch (Exception ex)
                {
                    if (ex is ReleaseDownloadException)
                    {
                        throw;
                    }

                    _logger.Trace(ex, "Unable to add manga report to download client: {0}", downloadClient.Definition.Name);
                    triedClients.Add(downloadClient.Definition.Id);
                }
            }

            throw new DownloadClientUnavailableException("All '{0}' download clients failed", remoteChapter.Release.DownloadProtocol);
        }

        private async Task DownloadReport(RemoteChapter remoteChapter, IDownloadClient downloadClient)
        {
            Ensure.That(remoteChapter.Manga, () => remoteChapter.Manga).IsNotNull();
            Ensure.That(remoteChapter.Chapters, () => remoteChapter.Chapters).HasItems();

            var downloadTitle = remoteChapter.Release.Title;

            if (downloadClient == null)
            {
                throw new DownloadClientUnavailableException($"{remoteChapter.Release.DownloadProtocol} Download client isn't configured yet");
            }

            // Limit grabs to 2 per second per host (mirrors TV DownloadService behavior).
            if (remoteChapter.Release.DownloadUrl.IsNotNullOrWhiteSpace() && !remoteChapter.Release.DownloadUrl.StartsWith("magnet:"))
            {
                var url = new HttpUri(remoteChapter.Release.DownloadUrl);
                await _rateLimitService.WaitAndPulseAsync(url.Host, TimeSpan.FromSeconds(2));
            }

            IIndexer indexer = null;

            if (remoteChapter.Release.IndexerId > 0)
            {
                indexer = _indexerFactory.GetInstance(_indexerFactory.Get(remoteChapter.Release.IndexerId));
            }

            string downloadClientId;
            try
            {
                downloadClientId = await DownloadViaClient(downloadClient, remoteChapter, indexer);
                _downloadClientStatusService.RecordSuccess(downloadClient.Definition.Id);
                _indexerStatusService.RecordSuccess(remoteChapter.Release.IndexerId);
            }
            catch (ReleaseUnavailableException)
            {
                _logger.Trace("Manga release {0} no longer available on indexer.", remoteChapter);
                throw;
            }
            catch (ReleaseBlockedException)
            {
                _logger.Trace("Manga release {0} previously added to blocklist, not sending to download client again.", remoteChapter);
                throw;
            }
            catch (DownloadClientRejectedReleaseException)
            {
                _logger.Trace("Manga release {0} rejected by download client, possible duplicate.", remoteChapter);
                throw;
            }
            catch (ReleaseDownloadException ex)
            {
                if (ex.InnerException is TooManyRequestsException http429)
                {
                    _indexerStatusService.RecordFailure(remoteChapter.Release.IndexerId, http429.RetryAfter);
                }
                else
                {
                    _indexerStatusService.RecordFailure(remoteChapter.Release.IndexerId);
                }

                throw;
            }

            // Publish the manga-sibling grab event. Phase 6 ChapterHistoryService.Handle
            // (Plan 06-03) writes a Grabbed history row in response. Phase 15 Wave (C)
            // collapses with EpisodeGrabbedEvent when Tv/ deletes.
            var chapterGrabbedEvent = new ChapterGrabbedEvent(
                remoteChapter,
                downloadClientId.IsNotNullOrWhiteSpace() ? downloadClientId : null,
                downloadClient.Definition.Name);

            _logger.ProgressInfo("Manga report sent to {0}. Indexer {1}. {2}",
                downloadClient.Definition.Name,
                remoteChapter.Release.Indexer,
                downloadTitle);

            _eventAggregator.PublishEvent(chapterGrabbedEvent);
        }

        // Wave (A) bridge: until W-6 lands the manga-shape Download(RemoteChapter, IIndexer)
        // overload on InProcessImageDownloadClient, this private helper routes through the
        // existing TV-shape IDownloadClient.Download(RemoteEpisode, IIndexer) via a LOCAL
        // shim. This shim is private to MangaDownloadService — it is NOT the public
        // RemoteChapter.ToRemoteEpisodeShim() static which Wave (A) W-5 deletes.
        //
        // Once W-6 ships the manga overload AND IDownloadClient adds Download(RemoteChapter,
        // IIndexer), this helper collapses to a direct call. For Wave (A) additive scope on
        // Mangarr-v0 we keep the bridge LOCAL so deleting the public shim is safe.
        private async Task<string> DownloadViaClient(IDownloadClient downloadClient, RemoteChapter remoteChapter, IIndexer indexer)
        {
            var shim = BuildLocalShim(remoteChapter);
            return await downloadClient.Download(shim, indexer);
        }

        private static NzbDrone.Core.Parser.Model.RemoteEpisode BuildLocalShim(RemoteChapter remoteChapter)
        {
            var manga = remoteChapter.Manga;
            var chapters = remoteChapter.Chapters ?? new List<NzbDrone.Core.Manga.Chapter>();

            var seriesShim = new NzbDrone.Core.Tv.Series { Id = manga?.Id ?? 0 };
            var episodeShims = chapters
                .Select(c => new NzbDrone.Core.Tv.Episode { Id = c.Id })
                .ToList();
            if (episodeShims.Count == 0)
            {
                episodeShims.Add(new NzbDrone.Core.Tv.Episode { Id = 0 });
            }

            return new NzbDrone.Core.Parser.Model.RemoteEpisode
            {
                Release = remoteChapter.Release,
                Series = seriesShim,
                Episodes = episodeShims,
                CustomFormats = remoteChapter.CustomFormats,
                CustomFormatScore = remoteChapter.CustomFormatScore
            };
        }
    }
}
