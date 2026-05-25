using System.Collections.Generic;
using NzbDrone.Common.Http.Proxy;

// Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — TV-only namespace imports stripped:
//   using NzbDrone.Core.ImportLists; ← deleted (ImportLists/ moved to .planning/reference/ per D-26)
//   using NzbDrone.Core.MediaFiles.EpisodeImport; ← deleted (subtree DELETED per Plan 15-10 A2)
//   using NzbDrone.Core.Qualities; ← deleted (Qualities/ DELETED per Plan 15-03)
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Security;

namespace NzbDrone.Core.Configuration
{
    public interface IConfigService
    {
        void SaveConfigDictionary(Dictionary<string, object> configValues);

        bool IsDefined(string key);

        // Download Client
        string DownloadClientWorkingFolders { get; set; }
        int DownloadClientHistoryLimit { get; set; }

        // Completed/Failed Download Handling (Download client)
        bool EnableCompletedDownloadHandling { get; set; }
        bool AutoRedownloadFailed { get; set; }
        bool AutoRedownloadFailedFromInteractiveSearch { get; set; }

        // Media Management
        bool AutoUnmonitorPreviouslyDownloadedEpisodes { get; set; }
        string RecycleBin { get; set; }
        int RecycleBinCleanupDays { get; set; }

        // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — ProperDownloadTypes (TV Quality cascade) stripped.
        //   ProperDownloadTypes DownloadPropersAndRepacks { get; set; }
        bool CreateEmptySeriesFolders { get; set; }
        bool DeleteEmptyFolders { get; set; }
        FileDateType FileDate { get; set; }
        bool SkipFreeSpaceCheckWhenImporting { get; set; }
        int MinimumFreeSpaceWhenImporting { get; set; }
        bool CopyUsingHardlinks { get; set; }
        bool EnableMediaInfo { get; set; }
        bool UseScriptImport { get; set; }
        string ScriptImportPath { get; set; }
        string CloudflareSolverUrl { get; set; }
        bool ImportExtraFiles { get; set; }
        string ExtraFileExtensions { get; set; }
        RescanAfterRefreshType RescanAfterRefresh { get; set; }

        // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — EpisodeTitleRequiredType (TV EpisodeImport) stripped.
        //   EpisodeTitleRequiredType EpisodeTitleRequired { get; set; }
        string UserRejectedExtensions { get; set; }

        // Sonarr divergence: Phase 17.3 Plan 17.3-05 (D-06) — SeasonPackUpgrade vertical stripped (manga has no season packs).
        //   SeasonPackUpgradeType SeasonPackUpgrade { get; set; }
        //   double SeasonPackUpgradeThreshold { get; set; }

        // Permissions (Media Management)
        bool SetPermissionsLinux { get; set; }
        string ChmodFolder { get; set; }
        string ChownGroup { get; set; }

        // Indexers
        int Retention { get; set; }
        int RssSyncInterval { get; set; }
        int MaximumSize { get; set; }
        int MinimumAge { get; set; }

        // Phase 26 Plan 26-04 (IL-03) — RESTORED. Consumed by ImportListSyncService
        // .TryCleanLibrary to drive the per-config response when a manga vanishes from
        // every enabled import list (Disabled / LogOnly / KeepAndUnmonitor / KeepAndTag).
        NzbDrone.Core.ImportLists.ListSyncLevelType ListSyncLevel { get; set; }
        int ListSyncTag { get; set; }

        // UI
        int FirstDayOfWeek { get; set; }
        string CalendarWeekColumnHeader { get; set; }

        string ShortDateFormat { get; set; }
        string LongDateFormat { get; set; }
        string TimeFormat { get; set; }
        string TimeZone { get; set; }
        bool ShowRelativeDates { get; set; }
        bool EnableColorImpairedMode { get; set; }
        int UILanguage { get; set; }

        // Internal
        bool CleanupMetadataImages { get; set; }
        string PlexClientIdentifier { get; }

        // Forms Auth
        string RijndaelPassphrase { get; }
        string HmacPassphrase { get; }
        string RijndaelSalt { get; }
        string HmacSalt { get; }

        // Proxy
        bool ProxyEnabled { get; }
        ProxyType ProxyType { get; }
        string ProxyHostname { get; }
        int ProxyPort { get; }
        string ProxyUsername { get; }
        string ProxyPassword { get; }
        string ProxyBypassFilter { get; }
        bool ProxyBypassLocalAddresses { get; }

        // Backups
        string BackupFolder { get; }
        int BackupInterval { get; }
        int BackupRetention { get; }

        CertificateValidationType CertificateValidation { get; }
        string ApplicationUrl { get; }

        // Phase 4 — global Config keys (D-04: NO Library entity, ever)
        string DownloadScratchPath { get; set; }
        string StagingPath { get; set; }
        string OutputFormat { get; set; }
        List<string> MetadataFormats { get; set; }
        int RetentionDays { get; set; }

        // Phase 5 — global default profile FKs (D-01 / D-07).
        // First-run UX dependency per D-11: empty default CF bundle means the seeded
        // TranslationProfile is the ONLY filter on first-run. Without these keys set,
        // per-Manga FK fallback fails. TranslationProfileService + CustomFormatProfileService
        // IHandle<ApplicationStartedEvent> seeders set them to the seeded default profile IDs.
        int? DefaultTranslationProfileId { get; set; }
        int? DefaultCustomFormatProfileId { get; set; }

        // Phase 6 — global RSS sync + auto-retry config keys (D-07 / D-13).
        // MangaRssSyncInterval default 15min (Sonarr RssSyncInterval default parity); per-IndexerDefinition.SyncInterval override on top.
        // MaxAutoRetriesPerChapter default 3 (D-13 bounded auto-retry against perpetual blocklist churn).
        int MangaRssSyncInterval { get; set; }
        int MaxAutoRetriesPerChapter { get; set; }
    }
}
