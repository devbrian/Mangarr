using System;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.MediaFiles.MangaImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Update.History.Events;

namespace NzbDrone.Core.Notifications
{
    // Sonarr divergence: Phase 15 W-1/W-2 cascade (CONTRACTS-AUDIT Wave A Cluster 2) -
    // TV-only IHandle implementations and helper methods removed:
    //  - IHandle<EpisodeGrabbedEvent>, IHandle<EpisodeImportedEvent>, IHandle<DownloadCompletedEvent>,
    //    IHandle<UntrackedDownloadCompletedEvent>, IHandle<SeriesRenamedEvent>,
    //    IHandle<SeriesAddCompletedEvent>, IHandle<SeriesDeletedEvent>, IHandle<EpisodeFileDeletedEvent>,
    //    IHandle<ManualInteractionRequiredEvent> all DELETED
    //  - GetMessage(Series, ...), GetFullSeasonMessage, GetQualityString, ShouldHandleSeries DELETED
    // Manga peers preserved: ChapterImportedEvent, MangaAddCompletedEvent, MangaDeletedEvent,
    // MangaRenamedEvent. Health + AppUpdate flows are media-agnostic and survive untouched.
    public class NotificationService
        : IHandle<HealthCheckFailedEvent>,
          IHandle<HealthCheckRestoredEvent>,
          IHandle<UpdateInstalledEvent>,
          IHandle<ChapterImportedEvent>,
          IHandle<MangaAddCompletedEvent>,
          IHandle<MangaDeletedEvent>,
          IHandle<MangaRenamedEvent>,
          IHandleAsync<DeleteCompletedEvent>,
          IHandleAsync<DownloadsProcessedEvent>,
          IHandleAsync<RenameCompletedEvent>,
          IHandleAsync<HealthCheckCompleteEvent>
    {
        private readonly INotificationFactory _notificationFactory;
        private readonly INotificationStatusService _notificationStatusService;
        private readonly Logger _logger;

        public NotificationService(INotificationFactory notificationFactory, INotificationStatusService notificationStatusService, Logger logger)
        {
            _notificationFactory = notificationFactory;
            _notificationStatusService = notificationStatusService;
            _logger = logger;
        }

        // Phase 8 Plan 99-08 - tag-filter discipline mirrors TV (sibling of deleted ShouldHandleSeries).
        private bool ShouldHandleManga(ProviderDefinition definition, NzbDrone.Core.Manga.Manga manga)
        {
            if (definition.Tags.Empty())
            {
                _logger.Debug("No tags set for this notification.");
                return true;
            }

            if (definition.Tags.Intersect(manga.Tags).Any())
            {
                _logger.Debug("Notification and manga have one or more intersecting tags.");
                return true;
            }

            _logger.Debug("{0} does not have any intersecting tags with {1}. Notification will not be sent.", definition.Name, manga.Title);
            return false;
        }

        private bool ShouldHandleHealthFailure(HealthCheck.HealthCheck healthCheck, bool includeWarnings)
        {
            if (healthCheck.Type == HealthCheckResult.Error)
            {
                return true;
            }

            if (healthCheck.Type == HealthCheckResult.Warning && includeWarnings)
            {
                return true;
            }

            return false;
        }

        public void Handle(MangaAddCompletedEvent message)
        {
            var addMessage = new MangaAddMessage
            {
                Manga = message.Manga,
                Message = message.Manga.Title
            };

            foreach (var notification in _notificationFactory.OnMangaAddEnabled())
            {
                try
                {
                    if (ShouldHandleManga(notification.Definition, message.Manga))
                    {
                        notification.OnMangaAdd(addMessage);
                        _notificationStatusService.RecordSuccess(notification.Definition.Id);
                    }
                }
                catch (Exception ex)
                {
                    _notificationStatusService.RecordFailure(notification.Definition.Id);
                    _logger.Warn(ex, "Unable to send OnMangaAdd notification to: " + notification.Definition.Name);
                }
            }
        }

        public void Handle(MangaDeletedEvent message)
        {
            var deleteMessage = new MangaDeleteMessage(message.Manga, message.DeleteFiles);

            foreach (var notification in _notificationFactory.OnMangaDeleteEnabled())
            {
                try
                {
                    if (ShouldHandleManga(notification.Definition, message.Manga))
                    {
                        notification.OnMangaDelete(deleteMessage);
                        _notificationStatusService.RecordSuccess(notification.Definition.Id);
                    }
                }
                catch (Exception ex)
                {
                    _notificationStatusService.RecordFailure(notification.Definition.Id);
                    _logger.Warn(ex, "Unable to send OnMangaDelete notification to: " + notification.Definition.Name);
                }
            }
        }

        public void Handle(MangaRenamedEvent message)
        {
            foreach (var notification in _notificationFactory.OnMangaRenameEnabled())
            {
                try
                {
                    if (ShouldHandleManga(notification.Definition, message.Manga))
                    {
                        notification.OnMangaRename(message.Manga, message.RenamedFiles);
                        _notificationStatusService.RecordSuccess(notification.Definition.Id);
                    }
                }
                catch (Exception ex)
                {
                    _notificationStatusService.RecordFailure(notification.Definition.Id);
                    _logger.Warn(ex, "Unable to send OnMangaRename notification to: " + notification.Definition.Name);
                }
            }
        }

        public void Handle(UpdateInstalledEvent message)
        {
            var updateMessage = new ApplicationUpdateMessage();
            updateMessage.Message = $"Sonarr updated from {message.PreviousVerison.ToString()} to {message.NewVersion.ToString()}";
            updateMessage.PreviousVersion = message.PreviousVerison;
            updateMessage.NewVersion = message.NewVersion;

            foreach (var notification in _notificationFactory.OnApplicationUpdateEnabled())
            {
                try
                {
                    notification.OnApplicationUpdate(updateMessage);
                    _notificationStatusService.RecordSuccess(notification.Definition.Id);
                }
                catch (Exception ex)
                {
                    _notificationStatusService.RecordFailure(notification.Definition.Id);
                    _logger.Warn(ex, "Unable to send OnApplicationUpdate notification to: " + notification.Definition.Name);
                }
            }
        }

        public void Handle(ChapterImportedEvent message)
        {
            if (!message.NewDownload)
            {
                return;
            }

            var msg = new ChapterImportMessage
            {
                Message = $"{message.Manga?.Title} - {message.Chapter?.Title}",
                Manga = message.Manga,
                Chapter = message.Chapter,
                ChapterFile = message.ChapterFile,
                SourceTitle = message.DownloadClientItem?.Title,
                SourcePath = message.SourcePath,
                DownloadClient = message.DownloadClientItem?.DownloadClientInfo?.Name,
                DownloadId = message.DownloadClientItem?.DownloadId,
                OldFiles = !message.NewDownload
            };

            foreach (var notification in _notificationFactory.OnChapterImportEnabled())
            {
                try
                {
                    if (!notification.SupportsOnChapterImport)
                    {
                        continue;
                    }

                    notification.OnChapterImport(msg);
                    _notificationStatusService.RecordSuccess(notification.Definition.Id);
                }
                catch (Exception ex)
                {
                    _notificationStatusService.RecordFailure(notification.Definition.Id);
                    _logger.Warn(ex, "Unable to send OnChapterImport notification to: " + notification.Definition.Name);
                }
            }
        }

        public void Handle(HealthCheckFailedEvent message)
        {
            if (message.IsInStartupGracePeriod)
            {
                return;
            }

            foreach (var notification in _notificationFactory.OnHealthIssueEnabled())
            {
                try
                {
                    if (ShouldHandleHealthFailure(message.HealthCheck, ((NotificationDefinition)notification.Definition).IncludeHealthWarnings))
                    {
                        notification.OnHealthIssue(message.HealthCheck);
                        _notificationStatusService.RecordSuccess(notification.Definition.Id);
                    }
                }
                catch (Exception ex)
                {
                    _notificationStatusService.RecordFailure(notification.Definition.Id);
                    _logger.Warn(ex, "Unable to send OnHealthIssue notification to: " + notification.Definition.Name);
                }
            }
        }

        public void Handle(HealthCheckRestoredEvent message)
        {
            if (message.IsInStartupGracePeriod)
            {
                return;
            }

            foreach (var notification in _notificationFactory.OnHealthRestoredEnabled())
            {
                try
                {
                    if (ShouldHandleHealthFailure(message.PreviousCheck, ((NotificationDefinition)notification.Definition).IncludeHealthWarnings))
                    {
                        notification.OnHealthRestored(message.PreviousCheck);
                        _notificationStatusService.RecordSuccess(notification.Definition.Id);
                    }
                }
                catch (Exception ex)
                {
                    _notificationStatusService.RecordFailure(notification.Definition.Id);
                    _logger.Warn(ex, "Unable to send OnHealthRestored notification to: " + notification.Definition.Name);
                }
            }
        }

        public void HandleAsync(DeleteCompletedEvent message)
        {
            ProcessQueue();
        }

        public void HandleAsync(DownloadsProcessedEvent message)
        {
            ProcessQueue();
        }

        public void HandleAsync(RenameCompletedEvent message)
        {
            ProcessQueue();
        }

        public void HandleAsync(HealthCheckCompleteEvent message)
        {
            ProcessQueue();
        }

        private void ProcessQueue()
        {
            foreach (var notification in _notificationFactory.GetAvailableProviders())
            {
                try
                {
                    notification.ProcessQueue();
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Unable to process notification queue for " + notification.Definition.Name);
                }
            }
        }
    }
}
