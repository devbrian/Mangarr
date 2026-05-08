using System;
using System.Collections.Generic;
using FluentValidation.Results;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Notifications
{
    // Sonarr divergence: Phase 15 W-2 (CONTRACTS-AUDIT  Wave A Cluster 2) - removed:
    //  - Virtual no-op overrides for the 8 TV-only INotification hooks (OnGrab, OnDownload,
    //    OnRename, OnEpisodeFileDelete, OnSeriesAdd, OnSeriesDelete, OnImportComplete,
    //    OnManualInteractionRequired)
    //  - Matching Supports* properties for those 8 hooks
    //  - EPISODE_/SERIES_ title constants (TV-only)
    //  - using NzbDrone.Core.Tv (orphan after the above)
    // Atomic with W-1 (INotification trim) and notifications-extra MOVE in Plan 15-04.
    public abstract class NotificationBase<TSettings> : INotification
        where TSettings : NotificationSettingsBase<TSettings>, new()
    {
        protected const string IMPORT_COMPLETE_TITLE = "Import Complete";
        protected const string HEALTH_ISSUE_TITLE = "Health Check Failure";
        protected const string HEALTH_RESTORED_TITLE = "Health Check Restored";
        protected const string APPLICATION_UPDATE_TITLE = "Application Updated";

        // Sonarr divergence: Phase 15 close-out (Plan 15-09 fix-forward) — branded notification
        // title prefixes flipped Sonarr → Mangarr. These are user-visible (push notification
        // body / email subject); Wave 4 \bSonarr\b string sweep missed them likely due to the
        // batch-list truncation surfaced in Plan 15-08 SUMMARY.
        protected const string IMPORT_COMPLETE_TITLE_BRANDED = "Mangarr - " + IMPORT_COMPLETE_TITLE;
        protected const string HEALTH_ISSUE_TITLE_BRANDED = "Mangarr - " + HEALTH_ISSUE_TITLE;
        protected const string HEALTH_RESTORED_TITLE_BRANDED = "Mangarr - " + HEALTH_RESTORED_TITLE;
        protected const string APPLICATION_UPDATE_TITLE_BRANDED = "Mangarr - " + APPLICATION_UPDATE_TITLE;

        public abstract string Name { get; }

        public Type ConfigContract => typeof(TSettings);

        public virtual ProviderMessage Message => null;

        public IEnumerable<ProviderDefinition> DefaultDefinitions => new List<ProviderDefinition>();

        public ProviderDefinition Definition { get; set; }
        public abstract ValidationResult Test();

        public abstract string Link { get; }

        public virtual void OnHealthIssue(HealthCheck.HealthCheck healthCheck)
        {
        }

        public virtual void OnHealthRestored(HealthCheck.HealthCheck previousCheck)
        {
        }

        public virtual void OnApplicationUpdate(ApplicationUpdateMessage updateMessage)
        {
        }

        // Phase 6 D-18 manga-shaped hook. Default no-op; providers (Komga/Kavita) override.
        public virtual void OnChapterImport(ChapterImportMessage message)
        {
        }

        // Phase 8 Plan 99-08 - manga library-state hooks (siblings of OnSeriesAdd/Delete/Rename).
        // Virtual no-ops; reflection-backed Supports* default to FALSE; v1.1+ providers override.
        public virtual void OnMangaAdd(MangaAddMessage message)
        {
        }

        public virtual void OnMangaDelete(MangaDeleteMessage deleteMessage)
        {
        }

        public virtual void OnMangaRename(NzbDrone.Core.Manga.Manga manga, List<RenamedChapterFile> renamedFiles)
        {
        }

        // Manga siblings of TV-deleted OnEpisodeFileDelete / OnEpisodeFileDeleteForUpgrade hooks.
        // Virtual no-ops; reflection-backed Supports* default to FALSE; v1.1+ providers override.
        // Surface-only (no v1 publisher) — see Notifications/ChapterFileDeleteMessage.cs header.
        public virtual void OnChapterFileDelete(ChapterFileDeleteMessage deleteMessage)
        {
        }

        public virtual void OnChapterFileDeleteForUpgrade(ChapterFileDeleteMessage deleteMessage)
        {
        }

        public virtual void ProcessQueue()
        {
        }

        public bool SupportsOnHealthIssue => HasConcreteImplementation("OnHealthIssue");
        public bool SupportsOnHealthRestored => HasConcreteImplementation("OnHealthRestored");
        public bool SupportsOnApplicationUpdate => HasConcreteImplementation("OnApplicationUpdate");
        public bool SupportsOnChapterImport => HasConcreteImplementation("OnChapterImport");
        public bool SupportsOnMangaAdd => HasConcreteImplementation("OnMangaAdd");
        public bool SupportsOnMangaDelete => HasConcreteImplementation("OnMangaDelete");
        public bool SupportsOnMangaRename => HasConcreteImplementation("OnMangaRename");
        public bool SupportsOnChapterFileDelete => HasConcreteImplementation("OnChapterFileDelete");
        public bool SupportsOnChapterFileDeleteForUpgrade => HasConcreteImplementation("OnChapterFileDeleteForUpgrade");

        protected TSettings Settings => (TSettings)Definition.Settings;

        public override string ToString()
        {
            return GetType().Name;
        }

        public virtual object RequestAction(string action, IDictionary<string, string> query)
        {
            return null;
        }

        private bool HasConcreteImplementation(string methodName)
        {
            var method = GetType().GetMethod(methodName);

            if (method == null)
            {
                throw new MissingMethodException(GetType().Name, Name);
            }

            return !method.DeclaringType.IsAbstract;
        }
    }
}
