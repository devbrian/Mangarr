using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Backup;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Configuration.Events;

// using NzbDrone.Core.DataAugmentation.Scene; // Sonarr divergence: Phase 15 D-19 — DataAugmentation/ deleted by Plan 15-04 (UpdateSceneMappingCommand registration stripped)
// using NzbDrone.Core.Download; // Sonarr divergence: Phase 15 fix-forward (debug-session refresh-monitored-downloads-di) — RefreshMonitoredDownloadsCommand defaultTasks row stripped (handler was on DownloadMonitoringService.cs deleted by Plan 15-10 per 15-10-SUMMARY:229); class file deleted in same fix; no remaining unqualified Download.* symbols in this file (Clients.InProcess + Manga sub-namespaces still imported below).
using NzbDrone.Core.Download.Manga;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.Housekeeping;

// Phase 26 Plan 26-04 RESTORED per D-15 — ImportListSyncCommand defaultTasks row landed at 24h cadence (Sonarr-canonical).
using NzbDrone.Core.ImportLists;

// using NzbDrone.Core.Indexers; // Sonarr divergence: Phase 15 D-10 — RssSyncCommand class deleted by Plan 15-04 (HandleAsync block stripped)
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
            // Sonarr divergence: Phase 15 D-19 + D-26 + D-10 + D-24 — TV defaultTasks registrations stripped:
            //   typeof(UpdateSceneMappingCommand).FullName ← deleted (DataAugmentation/Scene/ deleted Plan 15-04 per D-19)
            //   typeof(ImportListSyncCommand).FullName — Phase 15 D-26 stripped; Phase 26 Plan 26-04 RESTORED below (24h cadence, D-15)
            //   typeof(RssSyncCommand).FullName ← deleted Plan 15-03 (preemptive); class itself deleted Plan 15-04 per D-10
            //   typeof(RefreshSeriesCommand).FullName ← deleted Plan 15-03 per D-24
            //   typeof(RefreshMonitoredDownloadsCommand).FullName ← deleted by debug-session fix-forward
            //     `refresh-monitored-downloads-di` (per 15-10-SUMMARY:229 recommendation). The
            //     IExecute<RefreshMonitoredDownloadsCommand> handler lived on DownloadMonitoringService.cs
            //     which Plan 15-10 deleted (it co-implemented IHandle<EpisodeGrabbedEvent> + IHandle<EpisodeImportedEvent>
            //     for deleted TV events). The defaultTasks row + the orphan command class
            //     (NzbDrone.Core.Download/RefreshMonitoredDownloadsCommand.cs) were both stripped here;
            //     sibling orphans CheckForFinishedDownloadCommand + ProcessMonitoredDownloadsCommand
            //     deleted in the same fix (handlers on the same deleted DownloadMonitoringService /
            //     DownloadProcessingService; not in defaultTasks but dead code). The manga in-process
            //     completion poller (ProcessMangaCompletedCommand) that formerly covered this path was
            //     retired in Phase 39 RETIRE-01 — the Phase 36 monitoring loop
            //     (RefreshMonitoredMangaDownloadsCommand, below) now drives completed-download import.
            var defaultTasks = new List<ScheduledTask>
                {
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

                    // Sonarr divergence: Phase 15 D-19 — UpdateSceneMappingCommand defaultTasks row stripped by Plan 15-04
                    //   Class deleted in same plan (DataAugmentation/Scene/UpdateSceneMappingCommand.cs).

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

                    // Phase 39 RETIRE-01: the Phase-4 HousekeepInProcessDownloadsCommand daily
                    // housekeeping row was REMOVED with the in-process download vertical (the
                    // housekeeper + its command are deleted in this plan). TaskManager.Handle(
                    // ApplicationStartedEvent) self-reconciles the orphan ScheduledTasks DB row on
                    // next start (deletes rows whose TypeName no longer appears in defaultTasks).

                    // Phase 26 Plan 26-04 RESTORED per D-15 — ImportListSyncCommand row
                    // returns at the Sonarr-canonical 24h cadence. Substrate ships zero
                    // production providers (D-08); Phase 27 plugs in MangaDex / AniList /
                    // MyAnimeList plugins additively against this seam.
                    new ScheduledTask
                    {
                        Interval = 24 * 60,
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

                    // Phase 39 RETIRE-01: the Phase-6 in-process completion poller
                    // (ProcessMangaCompletedCommand, 1-min) was REMOVED with the in-process download
                    // vertical (the poller + command + ChapterArchivedEvent are deleted in this plan).
                    // The Phase 36 monitoring loop (RefreshMonitoredMangaDownloadsCommand, below) is
                    // the surviving completed-download driver. TaskManager.Handle(ApplicationStartedEvent)
                    // self-reconciles the orphan ScheduledTasks DB row on next start.

                    // Phase 36 LOOP-01 / LOOP-05 — 1-minute monitoring-loop poll heart. The
                    // MangaDownloadMonitoringService IExecute<RefreshMonitoredMangaDownloadsCommand>
                    // handler polls every DownloadHandlingEnabled() client, tracks each in-flight
                    // item, runs the Completed/Failed Checks, and publishes TrackedDownloadRefreshedEvent
                    // as the LAST step (the dead-queue fix — wakes the starved MangaQueueService → SignalR
                    // cascade), then pushes the queued-only ProcessMonitoredMangaDownloadsCommand at the
                    // tail. Registered at runtime via TaskManager.defaultTasks per sonarr-consistency-audit
                    // anti-pattern C (NOT seeded via 001 Insert.IntoTable). ProcessMonitoredMangaDownloads
                    // Command is deliberately NOT registered here — it is queued-only (Q-poll resolution).
                    new ScheduledTask
                    {
                        Interval = 1,
                        TypeName = typeof(RefreshMonitoredMangaDownloadsCommand).FullName,

                        // Sonarr divergence: the scheduled completion poll runs at High, NOT the
                        // ScheduledTask default (Low). Manga searches are minutes-long gateway scrapes
                        // pushed at Normal (CommandController / Missing+Cutoff search services); with only
                        // 3 worker threads (CommandExecutor.THREAD_LIMIT) a large search batch keeps every
                        // thread saturated for hours. Queue order is Priority-desc then FIFO
                        // (CommandQueue.TryGet), so a Low poll is outranked by every queued Normal search
                        // and never gets a thread — completed gateway downloads go undetected and imports
                        // pile up until the batch drains (priority inversion: this poll is the sole
                        // steady-state creator of the already-High ProcessMonitoredMangaDownloadsCommand).
                        // High lets the 1-minute poll preempt queued searches for the next freed thread, so
                        // imports flow continuously instead of all-at-end. Mirrors the event-driven
                        // MangaDownloadMonitoringService.QueueRefresh(), which already pushes this command
                        // at High. NOTE: kept after TypeName so the Interval=1/TypeName regex in
                        // TaskManagerDefaultTasksFixture stays matched.
                        Priority = CommandPriority.High
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

        // Sonarr divergence: Phase 15 D-10 — GetRssSyncInterval() removed; sole caller (HandleAsync RssSyncCommand
        //   rebroadcast) stripped in same edit. Manga peer GetMangaRssSyncInterval() below preserves the same
        //   clamp pattern (0 = disable, sub-10 floored to 10, anything else honored).

        // Phase 6 D-07 — global manga RSS sync default (Config.MangaRssSyncInterval, default 15min).
        // Clamp pattern (originally mirrored from deleted GetRssSyncInterval): 0 = disable, sub-10 floored to 10,
        // anything else honored. Per-IndexerDefinition.SyncInterval override applied at fetch time inside MangaRssSyncService.
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
            // Sonarr divergence: Phase 15 D-10 — typeof(RssSyncCommand) rebroadcast block stripped by Plan 15-04
            //   Class deleted in same plan (Indexers/RssSyncCommand.cs). Manga MangaRssSyncCommand rebroadcast
            //   preserved per WR-02 mitigation (Settings → MangaRssSyncInterval still applies without restart).

            var backup = _scheduledTaskRepository.GetDefinition(typeof(BackupCommand));
            backup.Interval = GetBackupInterval();

            // Phase 6 Plan 14 — WR-02 mitigation. Mirror the TV RssSyncCommand rebroadcast for
            // the manga sibling so Settings → MangaRssSyncInterval changes apply without restart.
            // PIPELINE-02 success criterion requires the interval to be configurable at runtime.
            var mangaRss = _scheduledTaskRepository.GetDefinition(typeof(MangaRssSyncCommand));
            mangaRss.Interval = GetMangaRssSyncInterval();

            _scheduledTaskRepository.UpdateMany(new List<ScheduledTask> { backup, mangaRss });

            _cache.Find(backup.TypeName).Interval = backup.Interval;
            _cache.Find(mangaRss.TypeName).Interval = mangaRss.Interval;
        }
    }
}
