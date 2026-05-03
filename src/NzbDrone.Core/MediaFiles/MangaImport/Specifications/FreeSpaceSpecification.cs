using System;
using System.IO;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;

namespace NzbDrone.Core.MediaFiles.MangaImport.Specifications
{
    // Sonarr divergence: NEW manga sibling per Phase 6 PIPELINE-04 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/MediaFiles/EpisodeImport/Specifications/
    // FreeSpaceSpecification.cs — copied verbatim with type swap (Series→Manga, Episode→Chapter).
    //
    // Reuses existing Config keys SkipFreeSpaceCheckWhenImporting + MinimumFreeSpaceWhenImporting
    // (no new manga keys; the threshold semantics are domain-agnostic).
    //
    // Phase 8 cleanup: collapse with TV FreeSpaceSpecification when Tv/ deletes.
    public class FreeSpaceSpecification : IMangaImportDecisionEngineSpecification
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public FreeSpaceSpecification(IDiskProvider diskProvider, IConfigService configService, Logger logger)
        {
            _diskProvider = diskProvider;
            _configService = configService;
            _logger = logger;
        }

        public MangaImportSpecDecision IsSatisfiedBy(LocalChapter localChapter, DownloadClientItem downloadClientItem)
        {
            if (_configService.SkipFreeSpaceCheckWhenImporting)
            {
                _logger.Debug("Skipping free space check when importing");
                return MangaImportSpecDecision.Accept();
            }

            try
            {
                if (localChapter.ExistingFile)
                {
                    _logger.Debug("Skipping free space check for existing chapter file");
                    return MangaImportSpecDecision.Accept();
                }

                if (localChapter.Manga == null || localChapter.Manga.Path.IsNullOrWhiteSpace())
                {
                    _logger.Debug("Manga path missing for free space check; accepting");
                    return MangaImportSpecDecision.Accept();
                }

                var path = Directory.GetParent(localChapter.Manga.Path);
                if (path == null)
                {
                    return MangaImportSpecDecision.Accept();
                }

                var freeSpace = _diskProvider.GetAvailableSpace(path.FullName);
                if (!freeSpace.HasValue)
                {
                    _logger.Debug("Free space check returned no value for {0}", path.FullName);
                    return MangaImportSpecDecision.Accept();
                }

                if (freeSpace < localChapter.Size + _configService.MinimumFreeSpaceWhenImporting.Megabytes())
                {
                    _logger.Warn("Not enough free space ({0}) to import: {1} ({2})", freeSpace, localChapter, localChapter.Size);
                    return MangaImportSpecDecision.Reject(ImportRejectionReason.MinimumFreeSpace, "Not enough free space");
                }
            }
            catch (DirectoryNotFoundException ex)
            {
                _logger.Error(ex, "Unable to check free disk space while importing");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unable to check free disk space while importing. {0}", localChapter.Path);
            }

            return MangaImportSpecDecision.Accept();
        }
    }
}
