using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation.Results;
using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Notifications
{
    public interface INotificationFactory : IProviderFactory<INotification, NotificationDefinition>
    {
        List<INotification> OnGrabEnabled(bool filterBlockedNotifications = true);
        List<INotification> OnDownloadEnabled(bool filterBlockedNotifications = true);
        List<INotification> OnUpgradeEnabled(bool filterBlockedNotifications = true);
        List<INotification> OnImportCompleteEnabled(bool filterBlockedNotifications = true);
        List<INotification> OnRenameEnabled(bool filterBlockedNotifications = true);
        List<INotification> OnSeriesAddEnabled(bool filterBlockedNotifications = true);
        List<INotification> OnSeriesDeleteEnabled(bool filterBlockedNotifications = true);
        List<INotification> OnEpisodeFileDeleteEnabled(bool filterBlockedNotifications = true);
        List<INotification> OnEpisodeFileDeleteForUpgradeEnabled(bool filterBlockedNotifications = true);
        List<INotification> OnHealthIssueEnabled(bool filterBlockedNotifications = true);
        List<INotification> OnHealthRestoredEnabled(bool filterBlockedNotifications = true);
        List<INotification> OnApplicationUpdateEnabled(bool filterBlockedNotifications = true);
        List<INotification> OnManualInteractionEnabled(bool filterBlockedNotifications = true);
        List<INotification> OnChapterImportEnabled(bool filterBlockedNotifications = true);
        List<INotification> OnMangaAddEnabled(bool filterBlockedNotifications = true);
        List<INotification> OnMangaDeleteEnabled(bool filterBlockedNotifications = true);
        List<INotification> OnMangaRenameEnabled(bool filterBlockedNotifications = true);
    }

    public class NotificationFactory : ProviderFactory<INotification, NotificationDefinition>, INotificationFactory
    {
        private readonly INotificationStatusService _notificationStatusService;
        private readonly Logger _logger;

        public NotificationFactory(INotificationStatusService notificationStatusService, INotificationRepository providerRepository, IEnumerable<INotification> providers, IServiceProvider container, IEventAggregator eventAggregator, Logger logger)
            : base(providerRepository, providers, container, eventAggregator, logger)
        {
            _notificationStatusService = notificationStatusService;
            _logger = logger;
        }

        protected override List<NotificationDefinition> Active()
        {
            return base.Active().Where(c => c.Enable).ToList();
        }

        public List<INotification> OnGrabEnabled(bool filterBlockedNotifications = true)
        {
            if (filterBlockedNotifications)
            {
                return FilterBlockedNotifications(GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnGrab)).ToList();
            }

            return GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnGrab).ToList();
        }

        public List<INotification> OnDownloadEnabled(bool filterBlockedNotifications = true)
        {
            if (filterBlockedNotifications)
            {
                return FilterBlockedNotifications(GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnDownload)).ToList();
            }

            return GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnDownload).ToList();
        }

        public List<INotification> OnUpgradeEnabled(bool filterBlockedNotifications = true)
        {
            if (filterBlockedNotifications)
            {
                return FilterBlockedNotifications(GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnUpgrade)).ToList();
            }

            return GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnUpgrade).ToList();
        }

        public List<INotification> OnImportCompleteEnabled(bool filterBlockedNotifications = true)
        {
            if (filterBlockedNotifications)
            {
                return FilterBlockedNotifications(GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnImportComplete)).ToList();
            }

            return GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnImportComplete).ToList();
        }

        public List<INotification> OnRenameEnabled(bool filterBlockedNotifications = true)
        {
            if (filterBlockedNotifications)
            {
                return FilterBlockedNotifications(GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnRename)).ToList();
            }

            return GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnRename).ToList();
        }

        public List<INotification> OnSeriesAddEnabled(bool filterBlockedNotifications = true)
        {
            if (filterBlockedNotifications)
            {
                return FilterBlockedNotifications(GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnSeriesAdd)).ToList();
            }

            return GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnSeriesAdd).ToList();
        }

        public List<INotification> OnSeriesDeleteEnabled(bool filterBlockedNotifications = true)
        {
            if (filterBlockedNotifications)
            {
                return FilterBlockedNotifications(GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnSeriesDelete)).ToList();
            }

            return GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnSeriesDelete).ToList();
        }

        public List<INotification> OnEpisodeFileDeleteEnabled(bool filterBlockedNotifications = true)
        {
            if (filterBlockedNotifications)
            {
                return FilterBlockedNotifications(GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnEpisodeFileDelete)).ToList();
            }

            return GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnEpisodeFileDelete).ToList();
        }

        public List<INotification> OnEpisodeFileDeleteForUpgradeEnabled(bool filterBlockedNotifications = true)
        {
            if (filterBlockedNotifications)
            {
                return FilterBlockedNotifications(GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnEpisodeFileDeleteForUpgrade)).ToList();
            }

            return GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnEpisodeFileDeleteForUpgrade).ToList();
        }

        public List<INotification> OnHealthIssueEnabled(bool filterBlockedNotifications = true)
        {
            if (filterBlockedNotifications)
            {
                return FilterBlockedNotifications(GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnHealthIssue)).ToList();
            }

            return GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnHealthIssue).ToList();
        }

        public List<INotification> OnHealthRestoredEnabled(bool filterBlockedNotifications = true)
        {
            if (filterBlockedNotifications)
            {
                return FilterBlockedNotifications(GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnHealthRestored)).ToList();
            }

            return GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnHealthRestored).ToList();
        }

        public List<INotification> OnApplicationUpdateEnabled(bool filterBlockedNotifications = true)
        {
            if (filterBlockedNotifications)
            {
                return FilterBlockedNotifications(GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnApplicationUpdate)).ToList();
            }

            return GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnApplicationUpdate).ToList();
        }

        public List<INotification> OnManualInteractionEnabled(bool filterBlockedNotifications = true)
        {
            if (filterBlockedNotifications)
            {
                return FilterBlockedNotifications(GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnManualInteractionRequired)).ToList();
            }

            return GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnManualInteractionRequired).ToList();
        }

        // Phase 6 D-18 — Definition-enabled + Supports-flag dual filter (Pitfall 7 mitigation).
        // Mirrors OnImportCompleteEnabled shape but adds the `n.SupportsOnChapterImport` guard
        // so non-manga TV providers (which inherit the virtual no-op) are skipped without
        // wasted invocation, even if a user accidentally toggles OnChapterImport on a TV
        // notification through some future bulk-edit UI.
        public List<INotification> OnChapterImportEnabled(bool filterBlockedNotifications = true)
        {
            if (filterBlockedNotifications)
            {
                return FilterBlockedNotifications(GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnChapterImport && n.SupportsOnChapterImport)).ToList();
            }

            return GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnChapterImport && n.SupportsOnChapterImport).ToList();
        }

        // Phase 8 Plan 99-08 — Definition-enabled + Supports-flag dual filter (Pitfall 7 mitigation).
        // No v1 provider overrides OnMangaAdd/Delete/Rename, so these return empty until v1.1+.
        public List<INotification> OnMangaAddEnabled(bool filterBlockedNotifications = true)
        {
            if (filterBlockedNotifications)
            {
                return FilterBlockedNotifications(GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnMangaAdd && n.SupportsOnMangaAdd)).ToList();
            }

            return GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnMangaAdd && n.SupportsOnMangaAdd).ToList();
        }

        public List<INotification> OnMangaDeleteEnabled(bool filterBlockedNotifications = true)
        {
            if (filterBlockedNotifications)
            {
                return FilterBlockedNotifications(GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnMangaDelete && n.SupportsOnMangaDelete)).ToList();
            }

            return GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnMangaDelete && n.SupportsOnMangaDelete).ToList();
        }

        public List<INotification> OnMangaRenameEnabled(bool filterBlockedNotifications = true)
        {
            if (filterBlockedNotifications)
            {
                return FilterBlockedNotifications(GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnMangaRename && n.SupportsOnMangaRename)).ToList();
            }

            return GetAvailableProviders().Where(n => ((NotificationDefinition)n.Definition).OnMangaRename && n.SupportsOnMangaRename).ToList();
        }

        private IEnumerable<INotification> FilterBlockedNotifications(IEnumerable<INotification> notifications)
        {
            var blockedNotifications = _notificationStatusService.GetBlockedProviders().ToDictionary(v => v.ProviderId, v => v);

            foreach (var notification in notifications)
            {
                if (blockedNotifications.TryGetValue(notification.Definition.Id, out var notificationStatus))
                {
                    _logger.Debug("Temporarily ignoring notification {0} till {1} due to recent failures.", notification.Definition.Name, notificationStatus.DisabledTill.Value.ToLocalTime());
                    continue;
                }

                yield return notification;
            }
        }

        public override void SetProviderCharacteristics(INotification provider, NotificationDefinition definition)
        {
            base.SetProviderCharacteristics(provider, definition);

            // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — TV-shape
            // Supports* properties (OnGrab / OnDownload / OnUpgrade / OnImportComplete /
            // OnRename / OnSeriesAdd|Delete / OnEpisodeFileDelete / OnEpisodeFileDeleteForUpgrade)
            // stripped per Plan 15-06 W-1/W-2 INotification + NotificationBase TV-hook trim.
            definition.SupportsOnHealthIssue = provider.SupportsOnHealthIssue;
            definition.SupportsOnHealthRestored = provider.SupportsOnHealthRestored;
            definition.SupportsOnApplicationUpdate = provider.SupportsOnApplicationUpdate;

            // Sonarr divergence: Phase 15 Plan 15-10 — SupportsOnManualInteractionRequired stripped (TV-shape).
            //   definition.SupportsOnManualInteractionRequired = provider.SupportsOnManualInteractionRequired;
            definition.SupportsOnChapterImport = provider.SupportsOnChapterImport;
            definition.SupportsOnMangaAdd = provider.SupportsOnMangaAdd;
            definition.SupportsOnMangaDelete = provider.SupportsOnMangaDelete;
            definition.SupportsOnMangaRename = provider.SupportsOnMangaRename;

            // Phase 6 Plan 14 — BL-01 mitigation. NotificationDefinition.OnChapterImport
            // defaults to true at the property level for ergonomic first-save UX (newly
            // added Komga/Kavita work without an extra checkbox), but on FRESH provider
            // creation (Id == 0) we scope the bit to the actual capability so TV
            // providers (Plex/Discord/Slack/Pushover etc.) are NOT silently Enable=true
            // via the OnChapterImport leg of NotificationDefinition.Enable. Existing user
            // configs (Id > 0) are untouched — this only affects first save.
            // Defense-in-depth: NotificationFactory.OnChapterImportEnabled (above) still
            // dual-filters on `Definition.OnChapterImport && n.SupportsOnChapterImport`.
            //
            // Phase 8 Plan 99-08 — same BL-01 pattern applied to manga library-state hooks.
            // No v1 provider overrides OnMangaAdd/Delete/Rename, so on first save the
            // toggles get scoped to FALSE; v1.1+ providers that DO override flip them to
            // TRUE on save without the user touching a checkbox.
            if (definition.Id == 0)
            {
                definition.OnChapterImport = provider.SupportsOnChapterImport;
                definition.OnMangaAdd = provider.SupportsOnMangaAdd;
                definition.OnMangaDelete = provider.SupportsOnMangaDelete;
                definition.OnMangaRename = provider.SupportsOnMangaRename;
            }
        }

        public override ValidationResult Test(NotificationDefinition definition)
        {
            var result = base.Test(definition);

            if (definition.Id == 0)
            {
                return result;
            }

            if (result == null || result.IsValid)
            {
                _notificationStatusService.RecordSuccess(definition.Id);
            }
            else
            {
                _notificationStatusService.RecordFailure(definition.Id);
            }

            return result;
        }
    }
}
