using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Backup;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients.InProcess;
using NzbDrone.Core.Download.Manga;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.Housekeeping;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch.Manga;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Manga.Commands;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

// using NzbDrone.Core.Tv.Commands; // Sonarr divergence: Phase 15 D-24 — Tv/Commands/ deleted by Plan 15-03
using NzbDrone.Core.Update.Commands;

namespace NzbDrone.Core.Jobs
{
    public interface ITaskManager
    {
        IList<ScheduledTask> GetPending();
        List<ScheduledTask> GetAll();
        DateTime GetNextExecution(Type type);
    }

    public class TaskManager : ITaskManager, IHandle<ApplicationStartedEvent>, IHandle<CommandExecutedEvent>, IHandleAsync<ConfigSavedEvent>
    {
        private readonly IScheduledTaskRepository _scheduledTaskRepository;
        private readonly IConfigService _configService;
        private readonly Logger _logger;
        private readonly ICached<ScheduledTask> _cache;

        public TaskManager(IScheduledTaskRepository scheduledTaskRepository, IConfigService configService, ICacheManager cacheManager, Logger logger)
        {
            _scheduledTaskRepository = scheduledTaskRepository;
            _configService = configService;
            _cache = cacheManager.GetCache<ScheduledTask>(GetType());
            _logger = logger;
        }

        public IList<ScheduledTask> GetPending()
        {
            return _cache.Values
                         .Where(c => c.Interval > 0 && c.LastExecution.AddMinutes(c.Interval) < DateTime.UtcNow)
                         .ToList();
        }

        public List<ScheduledTask> GetAll()
        {
            return _cache.Values.ToList();
        }

        public DateTime GetNextExecution(Type type)
        {
            var scheduledTask = _cache.Find(type.FullName);

            return scheduledTask.LastExecution.AddMinutes(scheduledTask.Interval);
        }

        public void Handle(ApplicationStartedEvent message)
        {
            var defaultTasks = new List<ScheduledTask>
                {
                    new ScheduledTask
                    {
                        Interval = 1,
                        TypeName = typeof(RefreshMonitoredDownloadsCommand).FullName,
                        Priority = CommandPriority.High
                    },

                    new ScheduledTask
                    {
                        Interval = 5,
                        TypeName = typeof(MessagingCleanupCommand).FullName
                    },

                    new ScheduledTask
                    {
                        Interval = 6 * 60,
                        TypeName = typeof(ApplicationUpdateCheckCommand).FullName
                    },

                    new ScheduledTask
                    {
                        Interval = 3 * 60,
                        TypeName = typeof(UpdateSceneMappingCommand).FullName
                    },

                    new ScheduledTask
                    {
                        Interval = 6 * 60,
                        TypeName = typeof(CheckHealthCommand).FullName
                    },

                    // Sonarr divergence: Phase 15 D-24 — RefreshSeriesCommand defaultTasks row stripped by Plan 15-03
                    //   Class itself deleted by Plan 15-03 Tv/Commands/RefreshSeriesCommand.cs delete.

                    // 12h refresh cadence per Phase 2 D-18. Mirrors RefreshSeriesCommand;
                    // manual trigger lands via Plan 02-09 dev endpoint and Phase 7 UI.
                    new ScheduledTask
                    {
                        Interval = 12 * 60,
                        TypeName = typeof(RefreshMangaCommand).FullName
                    },

                    new ScheduledTask
                    {
                        Interval = 24 * 60,
                        TypeName = typeof(HousekeepingCommand).FullName
                    },

                    new ScheduledTask
                    {
                        Interval = 24 * 60,
                        TypeName = typeof(CleanUpRecycleBinCommand).FullName
                    },

                    // Phase 4 — daily in-process downloader housekeeping (D-08 retention sweep
                    // + orphan scratch cleanup). Registered via TaskManager.defaultTasks per
                    // sonarr-consistency-audit anti-pattern C (NOT seeded via migration 001).
                    new ScheduledTask
                    {
                        Interval = 24 * 60,
                        TypeName = typeof(HousekeepInProcessDownloadsCommand).FullName
                    },

                    new ScheduledTask
                    {
                        Interval = 5,
                        TypeName = typeof(ImportListSyncCommand).FullName
                    },

                    new ScheduledTask
                    {
                        Interval = GetBackupInterval(),
                        TypeName = typeof(BackupCommand).FullName
                    },

                    // Sonarr divergence: Phase 15 D-24 — RssSyncCommand defaultTasks row stripped preemptively by Plan 15-03
                    //   per Phase 14 Wave 1b row 7 disposition. The class itself still exists at this wave
                    //   (deleted in Plan 15-04 Wave 1c); stripping the registration here prevents a Wave-1c-time
                    //   runtime crash where defaultTasks references a deleted command type.
                    //   The HandleAsync(ConfigSavedEvent) typeof(RssSyncCommand) rebroadcast block
                    //   is DEFERRED to Plan 15-04 (class still exists at this wave; line still parses).

                    // Phase 6 D-07 — manga RSS poll. Global default Config.MangaRssSyncInterval
                    // (15min); per-IndexerDefinition.SyncInterval override applied inside
                    // MangaRssSyncService.DueForRefresh. Registered at runtime via
                    // TaskManager.defaultTasks per sonarr-consistency-audit anti-pattern C
                    // (NOT seeded via 001 Insert.IntoTable).
                    new ScheduledTask
                    {
                        Interval = GetMangaRssSyncInterval(),
                        TypeName = typeof(MangaRssSyncCommand).FullName
                    },

                    // Phase 6 D-09 — daily Wanted/Missing sweep. Walks monitored Mangas
                    // with monitored unmet chapters; groups by MangaId; pushes one
                    // MangaSearchCommand per Manga. Registered at runtime via
                    // TaskManager.defaultTasks per sonarr-consistency-audit anti-pattern C.
                    new ScheduledTask
                    {
                        Interval = 24 * 60,
                        TypeName = typeof(MissingChapterSearchCommand).FullName
                    },

                    // Phase 6 RESEARCH Pattern 1 — 1-minute resilience poll path for
                    // ProcessMangaCompletedDownloads. The reactive IHandle<ChapterArchivedEvent>
                    // path covers the happy case; this scheduled poll covers (a) process
                    // restart between archive completion and import (event lost), (b) handler
                    // exception missed by event bus, (c) Phase 6 import-spec rejection where
                    // the row is held for retry. Registered at runtime via TaskManager.defaultTasks
                    // per sonarr-consistency-audit anti-pattern C (NOT seeded via 001 Insert.IntoTable).
                    new ScheduledTask
                    {
                        Interval = 1,
                        TypeName = typeof(ProcessMangaCompletedCommand).FullName
                    }
                };

            var currentTasks = _scheduledTaskRepository.All().ToList();

            _logger.Trace("Initializing jobs. Available: {0} Existing: {1}", defaultTasks.Count, currentTasks.Count);

            foreach (var job in currentTasks)
            {
                if (!defaultTasks.Any(c => c.TypeName == job.TypeName))
                {
                    _logger.Trace("Removing job from database '{0}'", job.TypeName);
                    _scheduledTaskRepository.Delete(job.Id);
                }
            }

            foreach (var defaultTask in defaultTasks)
            {
                var currentDefinition = currentTasks.SingleOrDefault(c => c.TypeName == defaultTask.TypeName) ?? defaultTask;

                currentDefinition.Interval = defaultTask.Interval;

                if (currentDefinition.Id == 0)
                {
                    currentDefinition.LastExecution = DateTime.UtcNow;
                }

                currentDefinition.Priority = defaultTask.Priority;

                _cache.Set(currentDefinition.TypeName, currentDefinition);
                _scheduledTaskRepository.Upsert(currentDefinition);
            }
        }

        private int GetBackupInterval()
        {
            var intervalMinutes = _configService.BackupInterval;

            if (intervalMinutes < 1)
            {
                intervalMinutes = 1;
            }

            if (intervalMinutes > 7)
            {
                intervalMinutes = 7;
            }

            return intervalMinutes * 60 * 24;
        }

        private int GetRssSyncInterval()
        {
            var interval = _configService.RssSyncInterval;

            if (interval > 0 && interval < 10)
            {
                return 10;
            }

            if (interval < 0)
            {
                return 0;
            }

            return interval;
        }

        // Phase 6 D-07 — global manga RSS sync default (Config.MangaRssSyncInterval, default 15min).
        // Mirrors GetRssSyncInterval clamp: 0 = disable, sub-10 floored to 10, anything else honored.
        // Per-IndexerDefinition.SyncInterval override applied at fetch time inside MangaRssSyncService.
        private int GetMangaRssSyncInterval()
        {
            var interval = _configService.MangaRssSyncInterval;

            if (interval > 0 && interval < 10)
            {
                return 10;
            }

            if (interval < 0)
            {
                return 0;
            }

            return interval;
        }

        public void Handle(CommandExecutedEvent message)
        {
            var scheduledTask = _scheduledTaskRepository.All().SingleOrDefault(c => c.TypeName == message.Command.Body.GetType().FullName);

            if (scheduledTask != null && message.Command.Body.UpdateScheduledTask)
            {
                _logger.Trace("Updating last run time for: {0}", scheduledTask.TypeName);

                var lastExecution = DateTime.UtcNow;
                var startTime = message.Command.StartedAt.Value;

                _scheduledTaskRepository.SetLastExecutionTime(scheduledTask.Id, lastExecution, startTime);

                var cached = _cache.Find(scheduledTask.TypeName);

                cached.LastExecution = lastExecution;
                cached.LastStartTime = startTime;
            }
        }

        public void HandleAsync(ConfigSavedEvent message)
        {
            var rss = _scheduledTaskRepository.GetDefinition(typeof(RssSyncCommand));
            rss.Interval = GetRssSyncInterval();

            var backup = _scheduledTaskRepository.GetDefinition(typeof(BackupCommand));
            backup.Interval = GetBackupInterval();

            // Phase 6 Plan 14 — WR-02 mitigation. Mirror the TV RssSyncCommand rebroadcast for
            // the manga sibling so Settings → MangaRssSyncInterval changes apply without restart.
            // PIPELINE-02 success criterion requires the interval to be configurable at runtime.
            var mangaRss = _scheduledTaskRepository.GetDefinition(typeof(MangaRssSyncCommand));
            mangaRss.Interval = GetMangaRssSyncInterval();

            _scheduledTaskRepository.UpdateMany(new List<ScheduledTask> { rss, backup, mangaRss });

            _cache.Find(rss.TypeName).Interval = rss.Interval;
            _cache.Find(backup.TypeName).Interval = backup.Interval;
            _cache.Find(mangaRss.TypeName).Interval = mangaRss.Interval;
        }
    }
}
