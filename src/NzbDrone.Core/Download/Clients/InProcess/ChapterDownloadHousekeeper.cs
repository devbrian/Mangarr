using System;
using System.Collections.Generic;
using System.IO;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Download.Clients.InProcess
{
    /// <summary>
    /// Phase 4 D-08 — daily housekeeper for the in-process downloader.
    ///
    /// Two responsibilities on execute:
    /// 1. Sweep <see cref="ChapterDownloadStatus.Failed"/> rows past <c>RetentionUntil</c>
    ///    (delegated to <see cref="IChapterDownloadStateRepository.DeleteOrphans"/>).
    /// 2. Scan <see cref="IConfigService.DownloadScratchPath"/> for orphan scratch dirs
    ///    (subdirs whose name does not correspond to a live DB row Id) and DeleteFolder them.
    ///
    /// Registered via <see cref="Jobs.TaskManager"/>.defaultTasks — NOT via migration seed
    /// (per <c>.claude/skills/sonarr-consistency-audit/SKILL.md</c> anti-pattern C; mirrors
    /// Phase 2 <c>RefreshMangaCommand</c> registration pattern). The <c>RegistrationFixture</c>
    /// guards this contract via in-memory SQLite.
    /// </summary>
    public class ChapterDownloadHousekeeper : IExecute<HousekeepInProcessDownloadsCommand>
    {
        private readonly IChapterDownloadStateRepository _stateRepo;
        private readonly IDiskProvider _diskProvider;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public ChapterDownloadHousekeeper(IChapterDownloadStateRepository stateRepo,
                                          IDiskProvider diskProvider,
                                          IConfigService configService,
                                          Logger logger)
        {
            _stateRepo = stateRepo;
            _diskProvider = diskProvider;
            _configService = configService;
            _logger = logger;
        }

        public void Execute(HousekeepInProcessDownloadsCommand message)
        {
            // 1. D-08 — sweep Failed rows past RetentionUntil. Catch + log: a transient repo
            //    failure must NOT prevent the orphan scratch sweep from running.
            try
            {
                _stateRepo.DeleteOrphans(DateTime.UtcNow);
                _logger.Debug("Phase 4 housekeeper: retention sweep complete");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Phase 4 housekeeper: retention sweep failed");
            }

            // 2. Orphan scratch cleanup — scan DownloadScratchPath for subdirs with no matching DB row.
            var scratchRoot = _configService.DownloadScratchPath;
            if (!_diskProvider.FolderExists(scratchRoot))
            {
                _logger.Trace("Phase 4 housekeeper: scratch root {0} absent; nothing to clean", scratchRoot);
                return;
            }

            // Project live row Ids to a string set so directory-name comparisons are O(1).
            var liveRowIds = new HashSet<string>();
            foreach (var row in _stateRepo.All())
            {
                liveRowIds.Add(row.Id.ToString());
            }

            try
            {
                foreach (var subdir in _diskProvider.GetDirectories(scratchRoot))
                {
                    var name = Path.GetFileName(subdir);
                    if (!liveRowIds.Contains(name))
                    {
                        _logger.Info("Phase 4 housekeeper: deleting orphan scratch dir {0}", subdir);
                        _diskProvider.DeleteFolder(subdir, true);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Phase 4 housekeeper: orphan scratch sweep failed");
            }
        }
    }
}
