using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http.Proxy;
using NzbDrone.Core.Configuration.Events;

// Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — TV-only namespace imports stripped:
//   using NzbDrone.Core.ImportLists; ← deleted (ImportLists/ moved to .planning/reference/ per D-26)
//   using NzbDrone.Core.MediaFiles.EpisodeImport; ← deleted (subtree DELETED per Plan 15-10 A2)
//   using NzbDrone.Core.Qualities; ← deleted (Qualities/ DELETED per Plan 15-03)
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Security;

namespace NzbDrone.Core.Configuration
{
    public enum ConfigKey
    {
        DownloadedEpisodesFolder
    }

    public class ConfigService : IConfigService
    {
        private readonly IConfigRepository _repository;
        private readonly IEventAggregator _eventAggregator;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly Logger _logger;
        private static Dictionary<string, string> _cache;

        public ConfigService(IConfigRepository repository, IEventAggregator eventAggregator, IAppFolderInfo appFolderInfo, Logger logger)
        {
            _repository = repository;
            _eventAggregator = eventAggregator;
            _appFolderInfo = appFolderInfo;
            _logger = logger;
            _cache = new Dictionary<string, string>();
        }

        private Dictionary<string, object> AllWithDefaults()
        {
            var dict = new Dictionary<string, object>(StringComparer.InvariantCultureIgnoreCase);

            var type = GetType();
            var properties = type.GetProperties();

            foreach (var propertyInfo in properties)
            {
                var value = propertyInfo.GetValue(this, null);
                dict.Add(propertyInfo.Name, value);
            }

            return dict;
        }

        public void SaveConfigDictionary(Dictionary<string, object> configValues)
        {
            var allWithDefaults = AllWithDefaults();
            var hasUpdated = false;

            foreach (var configValue in configValues)
            {
                allWithDefaults.TryGetValue(configValue.Key, out var currentValue);
                if (currentValue == null || configValue.Value == null)
                {
                    continue;
                }

                var equal = configValue.Value.ToString().Equals(currentValue.ToString());

                if (!equal)
                {
                    hasUpdated = true;
                    SetValue(configValue.Key, configValue.Value.ToString());
                }
            }

            if (hasUpdated)
            {
                _eventAggregator.PublishEvent(new ConfigSavedEvent());
            }
        }

        public bool IsDefined(string key)
        {
            return _repository.Get(key.ToLower()) != null;
        }

        public bool AutoUnmonitorPreviouslyDownloadedEpisodes
        {
            get { return GetValueBoolean("AutoUnmonitorPreviouslyDownloadedEpisodes"); }
            set { SetValue("AutoUnmonitorPreviouslyDownloadedEpisodes", value); }
        }

        public int Retention
        {
            get { return GetValueInt("Retention", 0); }
            set { SetValue("Retention", value); }
        }

        public string RecycleBin
        {
            get { return GetValue("RecycleBin", string.Empty); }
            set { SetValue("RecycleBin", value); }
        }

        public int RecycleBinCleanupDays
        {
            get { return GetValueInt("RecycleBinCleanupDays", 7); }
            set { SetValue("RecycleBinCleanupDays", value); }
        }

        public int RssSyncInterval
        {
            get { return GetValueInt("RssSyncInterval", 15); }

            set { SetValue("RssSyncInterval", value); }
        }

        public int MaximumSize
        {
            get { return GetValueInt("MaximumSize", 0); }
            set { SetValue("MaximumSize", value); }
        }

        public int MinimumAge
        {
            get { return GetValueInt("MinimumAge", 0); }

            set { SetValue("MinimumAge", value); }
        }

        // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption —
        // ProperDownloadTypes (TV-shape Quality cascade) stripped per Plan 15-03 Quality DELETE.
        //   public ProperDownloadTypes DownloadPropersAndRepacks { ... }

        public bool EnableCompletedDownloadHandling
        {
            get { return GetValueBoolean("EnableCompletedDownloadHandling", true); }

            set { SetValue("EnableCompletedDownloadHandling", value); }
        }

        public bool AutoRedownloadFailed
        {
            get { return GetValueBoolean("AutoRedownloadFailed", true); }

            set { SetValue("AutoRedownloadFailed", value); }
        }

        public bool AutoRedownloadFailedFromInteractiveSearch
        {
            get { return GetValueBoolean("AutoRedownloadFailedFromInteractiveSearch", true); }

            set { SetValue("AutoRedownloadFailedFromInteractiveSearch", value); }
        }

        public bool CreateEmptySeriesFolders
        {
            get { return GetValueBoolean("CreateEmptySeriesFolders", false); }

            set { SetValue("CreateEmptySeriesFolders", value); }
        }

        public bool DeleteEmptyFolders
        {
            get { return GetValueBoolean("DeleteEmptyFolders", false); }

            set { SetValue("DeleteEmptyFolders", value); }
        }

        public FileDateType FileDate
        {
            get { return GetValueEnum("FileDate", FileDateType.None); }

            set { SetValue("FileDate", value); }
        }

        public string DownloadClientWorkingFolders
        {
            get { return GetValue("DownloadClientWorkingFolders", "_UNPACK_|_FAILED_"); }
            set { SetValue("DownloadClientWorkingFolders", value); }
        }

        public int DownloadClientHistoryLimit
        {
            get { return GetValueInt("DownloadClientHistoryLimit", 60); }

            set { SetValue("DownloadClientHistoryLimit", value); }
        }

        // TODO: Rename to 'Skip Free Space Check'. Cosmetic rename only; deferred because the config KEY
        // ("SkipFreeSpaceCheckWhenImporting") is persisted in the Config table, so renaming the property requires
        // a Config-table migration. Trigger: do it when the Settings -> MediaManagement copy is next touched
        // (so the user-facing label and the property name change together). Tracked: post-v1.1-tracker DD-02.
        public bool SkipFreeSpaceCheckWhenImporting
        {
            get { return GetValueBoolean("SkipFreeSpaceCheckWhenImporting", false); }

            set { SetValue("SkipFreeSpaceCheckWhenImporting", value); }
        }

        public int MinimumFreeSpaceWhenImporting
        {
            get { return GetValueInt("MinimumFreeSpaceWhenImporting", 100); }

            set { SetValue("MinimumFreeSpaceWhenImporting", value); }
        }

        public bool CopyUsingHardlinks
        {
            get { return GetValueBoolean("CopyUsingHardlinks", true); }

            set { SetValue("CopyUsingHardlinks", value); }
        }

        public bool EnableMediaInfo
        {
            get { return GetValueBoolean("EnableMediaInfo", true); }

            set { SetValue("EnableMediaInfo", value); }
        }

        public bool UseScriptImport
        {
            get { return GetValueBoolean("UseScriptImport", false); }

            set { SetValue("UseScriptImport", value); }
        }

        public string ScriptImportPath
        {
            get { return GetValue("ScriptImportPath"); }

            set { SetValue("ScriptImportPath", value); }
        }

        public string CloudflareSolverUrl
        {
            get { return GetValue("CloudflareSolverUrl", string.Empty); }

            set { SetValue("CloudflareSolverUrl", value); }
        }

        public bool ImportExtraFiles
        {
            get { return GetValueBoolean("ImportExtraFiles", false); }

            set { SetValue("ImportExtraFiles", value); }
        }

        public string ExtraFileExtensions
        {
            get { return GetValue("ExtraFileExtensions", "srt"); }

            set { SetValue("ExtraFileExtensions", value); }
        }

        public RescanAfterRefreshType RescanAfterRefresh
        {
            get { return GetValueEnum("RescanAfterRefresh", RescanAfterRefreshType.Always); }

            set { SetValue("RescanAfterRefresh", value); }
        }

        // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption —
        // EpisodeTitleRequiredType (TV-shape MediaFiles/EpisodeImport) stripped per Plan 15-10 A2.
        //   public EpisodeTitleRequiredType EpisodeTitleRequired { ... }

        public string UserRejectedExtensions
        {
            get { return GetValue("UserRejectedExtensions", string.Empty); }
            set { SetValue("UserRejectedExtensions", value); }
        }

        // Sonarr divergence: Phase 17.3 Plan 17.3-05 (D-06) — SeasonPackUpgrade vertical stripped (manga has no season packs).
        //   public SeasonPackUpgradeType SeasonPackUpgrade { get; set; }  // backed by Config row "SeasonPackUpgrade" (stale rows OK pre-v1 per feedback_db_wipe_no_backup)
        //   public double SeasonPackUpgradeThreshold { get; set; }  // backed by Config row "SeasonPackUpgradeThreshold"

        public bool SetPermissionsLinux
        {
            get { return GetValueBoolean("SetPermissionsLinux", false); }

            set { SetValue("SetPermissionsLinux", value); }
        }

        public string ChmodFolder
        {
            get { return GetValue("ChmodFolder", "755"); }

            set { SetValue("ChmodFolder", value); }
        }

        public string ChownGroup
        {
            get { return GetValue("ChownGroup", ""); }

            set { SetValue("ChownGroup", value); }
        }

        // Phase 26 Plan 26-04 (IL-03) — RESTORED. Default is Disabled (Sonarr-canonical):
        // existing manga library is untouched unless the user explicitly opts in to
        // KeepAndUnmonitor / KeepAndTag / LogOnly.
        public NzbDrone.Core.ImportLists.ListSyncLevelType ListSyncLevel
        {
            get { return GetValueEnum("ListSyncLevel", NzbDrone.Core.ImportLists.ListSyncLevelType.Disabled); }
            set { SetValue("ListSyncLevel", value); }
        }

        public int ListSyncTag
        {
            get { return GetValueInt("ListSyncTag"); }
            set { SetValue("ListSyncTag", value); }
        }

        public int FirstDayOfWeek
        {
            get { return GetValueInt("FirstDayOfWeek", (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek); }

            set { SetValue("FirstDayOfWeek", value); }
        }

        public string CalendarWeekColumnHeader
        {
            get { return GetValue("CalendarWeekColumnHeader", "ddd M/D"); }

            set { SetValue("CalendarWeekColumnHeader", value); }
        }

        public string ShortDateFormat
        {
            get { return GetValue("ShortDateFormat", "MMM D YYYY"); }

            set { SetValue("ShortDateFormat", value); }
        }

        public string LongDateFormat
        {
            get { return GetValue("LongDateFormat", "dddd, MMMM D YYYY"); }

            set { SetValue("LongDateFormat", value); }
        }

        public string TimeFormat
        {
            get { return GetValue("TimeFormat", "h(:mm)a"); }

            set { SetValue("TimeFormat", value); }
        }

        public string TimeZone
        {
            get { return GetValue("TimeZone", ""); }

            set { SetValue("TimeZone", value); }
        }

        public bool ShowRelativeDates
        {
            get { return GetValueBoolean("ShowRelativeDates", true); }

            set { SetValue("ShowRelativeDates", value); }
        }

        public bool EnableColorImpairedMode
        {
            get { return GetValueBoolean("EnableColorImpairedMode", false); }

            set { SetValue("EnableColorImpairedMode", value); }
        }

        public int UILanguage
        {
            get { return GetValueInt("UILanguage", (int)Language.English); }

            set { SetValue("UILanguage", value); }
        }

        public bool CleanupMetadataImages
        {
            get { return GetValueBoolean("CleanupMetadataImages", true); }

            set { SetValue("CleanupMetadataImages", value); }
        }

        public string PlexClientIdentifier => GetValue("PlexClientIdentifier", Guid.NewGuid().ToString(), true);

        public string RijndaelPassphrase => GetValue("RijndaelPassphrase", Guid.NewGuid().ToString(), true);

        public string HmacPassphrase => GetValue("HmacPassphrase", Guid.NewGuid().ToString(), true);

        public string RijndaelSalt => GetValue("RijndaelSalt", Guid.NewGuid().ToString(), true);

        public string HmacSalt => GetValue("HmacSalt", Guid.NewGuid().ToString(), true);

        public bool ProxyEnabled => GetValueBoolean("ProxyEnabled", false);

        public ProxyType ProxyType => GetValueEnum<ProxyType>("ProxyType", ProxyType.Http);

        public string ProxyHostname => GetValue("ProxyHostname", string.Empty);

        public int ProxyPort => GetValueInt("ProxyPort", 8080);

        public string ProxyUsername => GetValue("ProxyUsername", string.Empty);

        public string ProxyPassword => GetValue("ProxyPassword", string.Empty);

        public string ProxyBypassFilter => GetValue("ProxyBypassFilter", string.Empty);

        public bool ProxyBypassLocalAddresses => GetValueBoolean("ProxyBypassLocalAddresses", true);

        public string BackupFolder => GetValue("BackupFolder", "Backups");

        public int BackupInterval => GetValueInt("BackupInterval", 7);

        public int BackupRetention => GetValueInt("BackupRetention", 28);

        public CertificateValidationType CertificateValidation =>
            GetValueEnum("CertificateValidation", CertificateValidationType.Enabled);

        public string ApplicationUrl => GetValue("ApplicationUrl", string.Empty);

        public bool TrustCgnatIpAddresses
        {
            get { return GetValueBoolean("TrustCgnatIpAddresses", false); }
            set { SetValue("TrustCgnatIpAddresses", value); }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Phase 4 — global Config keys (D-04: global, NO Library entity).
        // Surfaced in Phase 7 React Settings → Media Management form.
        // ─────────────────────────────────────────────────────────────────────

        public string DownloadScratchPath
        {
            get
            {
                var raw = GetValue("DownloadScratchPath", string.Empty);
                if (string.IsNullOrWhiteSpace(raw))
                {
                    // D-06 default — <DataDir>/scratch/downloads/. Cross-volume deployment supported.
                    var dataFolder = _appFolderInfo?.AppDataFolder ?? string.Empty;
                    return Path.Combine(dataFolder, "scratch", "downloads");
                }

                return raw;
            }
            set
            {
                SetValue("DownloadScratchPath", value);
            }
        }

        public string StagingPath
        {
            get
            {
                var raw = GetValue("StagingPath", string.Empty);
                if (string.IsNullOrWhiteSpace(raw))
                {
                    // Phase 4 D-09 — staging dir for finished CBZ artifacts (Phase 6 reads from here).
                    // Default <DataDir>/completed — sibling to scratch, NOT under it. Resolves T-04-22
                    // (mitigates the cross-volume / relative-".." path-construction risk that the
                    // initial plan introduced). Same volume as DataDir → atomic rename works.
                    var dataFolder = _appFolderInfo?.AppDataFolder ?? string.Empty;
                    return Path.Combine(dataFolder, "completed");
                }

                return raw;
            }
            set
            {
                SetValue("StagingPath", value);
            }
        }

        public string OutputFormat
        {
            get { return GetValue("OutputFormat", "cbz"); }   // ARCHIVE-01 default
            set { SetValue("OutputFormat", value); }
        }

        public List<string> MetadataFormats
        {
            get
            {
                // Default seeded only when key absent; once explicitly set we honor the stored
                // value (including null/empty list) so the round-trip in ConfigService
                // reflection tests is preserved.
                var raw = GetValue("MetadataFormats", "[\"comicinfo\"]");
                if (string.IsNullOrEmpty(raw))
                {
                    return null;
                }

                try
                {
                    return JsonConvert.DeserializeObject<List<string>>(raw);
                }
                catch (JsonException)
                {
                    return new List<string> { "comicinfo" };
                }
            }
            set
            {
                // Round-trip null through JSON so `set null` → `get null` (D-04 reflection test contract).
                SetValue("MetadataFormats", JsonConvert.SerializeObject(value));
            }
        }

        public int RetentionDays
        {
            get { return GetValueInt("RetentionDays", 7); }   // D-08 default
            set { SetValue("RetentionDays", value); }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Phase 5 — global default profile FKs (D-01 / D-07).
        // Null-round-trip pattern mirrors Phase 4 MetadataFormats (Pitfall 9 mitigation):
        //   set null => stored as empty string => get returns null. Reflection-based
        //   ConfigService round-trip tests rely on this contract.
        // ─────────────────────────────────────────────────────────────────────

        public int? DefaultTranslationProfileId
        {
            // WR-06: TryParse + InvariantCulture per Pitfall 7 — a corrupted Config row
            // (manual DB edit, encoding issue, leftover non-numeric junk like "null" or "0   ")
            // returned null instead of throwing FormatException up to every caller.
            get
            {
                var raw = GetValue("DefaultTranslationProfileId", string.Empty);
                if (string.IsNullOrEmpty(raw))
                {
                    return null;
                }

                return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                    ? id
                    : (int?)null;
            }
            set
            {
                SetValue("DefaultTranslationProfileId", value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
            }
        }

        public int? DefaultCustomFormatProfileId
        {
            // WR-06: TryParse + InvariantCulture per Pitfall 7 — see DefaultTranslationProfileId.
            get
            {
                var raw = GetValue("DefaultCustomFormatProfileId", string.Empty);
                if (string.IsNullOrEmpty(raw))
                {
                    return null;
                }

                return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                    ? id
                    : (int?)null;
            }
            set
            {
                SetValue("DefaultCustomFormatProfileId", value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Phase 6 — global RSS sync + auto-retry Config keys.
        // Lazy default-on-getter via GetValueInt — matches Sonarr's RssSyncInterval pattern (line 117-122).
        // ─────────────────────────────────────────────────────────────────────

        public int MangaRssSyncInterval
        {
            // D-07 default 15 min (parity with Sonarr's RssSyncInterval default).
            // Per-IndexerDefinition.SyncInterval overrides this on a per-source basis.
            get { return GetValueInt("MangaRssSyncInterval", 15); }
            set { SetValue("MangaRssSyncInterval", value); }
        }

        public int MaxAutoRetriesPerChapter
        {
            // D-13 default 3. Bounded auto-retry: after N alternates exhausted, chapter sits in
            // History as DownloadFailed; user manually retries from History row (HISTORY-03).
            get { return GetValueInt("MaxAutoRetriesPerChapter", 3); }
            set { SetValue("MaxAutoRetriesPerChapter", value); }
        }

        private string GetValue(string key)
        {
            return GetValue(key, string.Empty);
        }

        private bool GetValueBoolean(string key, bool defaultValue = false)
        {
            return Convert.ToBoolean(GetValue(key, defaultValue));
        }

        private int GetValueInt(string key, int defaultValue = 0)
        {
            return Convert.ToInt32(GetValue(key, defaultValue));
        }

        private double GetValueDouble(string key, double defaultValue = 0)
        {
            return Convert.ToDouble(GetValue(key, defaultValue), CultureInfo.InvariantCulture);
        }

        private T GetValueEnum<T>(string key, T defaultValue)
        {
            return (T)Enum.Parse(typeof(T), GetValue(key, defaultValue), true);
        }

        public string GetValue(string key, object defaultValue, bool persist = false)
        {
            key = key.ToLowerInvariant();
            Ensure.That(key, () => key).IsNotNullOrWhiteSpace();

            EnsureCache();

            if (_cache.TryGetValue(key, out var dbValue) && dbValue != null && !string.IsNullOrEmpty(dbValue))
            {
                return dbValue;
            }

            _logger.Trace("Using default config value for '{0}' defaultValue:'{1}'", key, defaultValue);

            if (persist)
            {
                SetValue(key, defaultValue.ToString());
            }

            return defaultValue.ToString();
        }

        private void SetValue(string key, bool value)
        {
            SetValue(key, value.ToString());
        }

        private void SetValue(string key, int value)
        {
            SetValue(key, value.ToString());
        }

        private void SetValue(string key, double value)
        {
            SetValue(key, value.ToString(CultureInfo.InvariantCulture));
        }

        private void SetValue(string key, Enum value)
        {
            SetValue(key, value.ToString().ToLower());
        }

        private void SetValue(string key, string value)
        {
            key = key.ToLowerInvariant();

            _logger.Trace("Writing Setting to database. Key:'{0}' Value:'{1}'", key, value);
            _repository.Upsert(key, value);

            ClearCache();
        }

        private void EnsureCache()
        {
            lock (_cache)
            {
                if (!_cache.Any())
                {
                    var all = _repository.All();
                    _cache = all.ToDictionary(c => c.Key.ToLower(), c => c.Value);
                }
            }
        }

        private static void ClearCache()
        {
            lock (_cache)
            {
                _cache = new Dictionary<string, string>();
            }
        }
    }
}
