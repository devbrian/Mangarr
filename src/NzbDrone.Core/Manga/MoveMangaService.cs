using System.IO;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Manga.Commands;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Organizer.Manga;

namespace NzbDrone.Core.Manga
{
    // Manga sibling of Mangarr's MoveSeriesService (Tv/MoveSeriesService.cs).
    // Mirrors the dual-handler pattern verbatim with type swaps:
    //   Series→Manga, ISeriesService→IMangaService, IBuildFileNames→IBuildMangaFileNames,
    //   SeriesMovedEvent→MangaMovedEvent, GetSeriesFolder→GetMangaFolder.
    //
    // No public interface (TV's MoveSeriesService has none either — DryIoc auto-wires the
    // concrete class through IExecute<TCommand> dispatch).
    public class MoveMangaService : IExecute<MoveMangaCommand>, IExecute<BulkMoveMangaCommand>
    {
        private readonly IMangaService _mangaService;
        private readonly IBuildMangaFileNames _filenameBuilder;
        private readonly IDiskProvider _diskProvider;
        private readonly IDiskTransferService _diskTransferService;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public MoveMangaService(IMangaService mangaService,
                                IBuildMangaFileNames filenameBuilder,
                                IDiskProvider diskProvider,
                                IDiskTransferService diskTransferService,
                                IEventAggregator eventAggregator,
                                Logger logger)
        {
            _mangaService = mangaService;
            _filenameBuilder = filenameBuilder;
            _diskProvider = diskProvider;
            _diskTransferService = diskTransferService;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        private void MoveSingleManga(Manga manga, string sourcePath, string destinationPath, int? index = null, int? total = null)
        {
            if (!sourcePath.IsPathValid(PathValidationType.CurrentOs))
            {
                _logger.Warn("Folder '{0}' for '{1}' is invalid, unable to move manga. Try moving files manually", sourcePath, manga.Title);
                return;
            }

            if (!_diskProvider.FolderExists(sourcePath))
            {
                _logger.Debug("Folder '{0}' for '{1}' does not exist, not moving.", sourcePath, manga.Title);
                return;
            }

            if (index != null && total != null)
            {
                _logger.ProgressInfo("Moving {0} from '{1}' to '{2}' ({3}/{4})", manga.Title, sourcePath, destinationPath, index + 1, total);
            }
            else
            {
                _logger.ProgressInfo("Moving {0} from '{1}' to '{2}'", manga.Title, sourcePath, destinationPath);
            }

            if (sourcePath.PathEquals(destinationPath))
            {
                _logger.ProgressInfo("{0} is already in the specified location '{1}'.", manga, destinationPath);
                return;
            }

            try
            {
                // Ensure the parent of the manga folder exists, this will often just be the root folder, but
                // in cases where people are using subfolders for first letter (etc) it may not yet exist.
                _diskProvider.CreateFolder(new DirectoryInfo(destinationPath).Parent.FullName);
                _diskTransferService.TransferFolder(sourcePath, destinationPath, TransferMode.Move);

                _logger.ProgressInfo("{0} moved successfully to {1}", manga.Title, destinationPath);

                _eventAggregator.PublishEvent(new MangaMovedEvent(manga, sourcePath, destinationPath));
            }
            catch (IOException ex)
            {
                _logger.Error(ex, "Unable to move manga from '{0}' to '{1}'. Try moving files manually", sourcePath, destinationPath);

                RevertPath(manga.Id, sourcePath);
            }
        }

        private void RevertPath(int mangaId, string path)
        {
            var manga = _mangaService.GetManga(mangaId);

            manga.Path = path;
            _mangaService.UpdateManga(manga);
        }

        public void Execute(MoveMangaCommand message)
        {
            var manga = _mangaService.GetManga(message.MangaId);
            MoveSingleManga(manga, message.SourcePath, message.DestinationPath);
        }

        public void Execute(BulkMoveMangaCommand message)
        {
            var mangaToMove = message.Manga;
            var destinationRootFolder = message.DestinationRootFolder;

            _logger.ProgressInfo("Moving {0} manga to '{1}'", mangaToMove.Count, destinationRootFolder);

            for (var index = 0; index < mangaToMove.Count; index++)
            {
                var m = mangaToMove[index];
                var manga = _mangaService.GetManga(m.MangaId);
                var destinationPath = Path.Combine(destinationRootFolder, _filenameBuilder.GetMangaFolder(manga));

                MoveSingleManga(manga, m.SourcePath, destinationPath, index, mangaToMove.Count);
            }

            _logger.ProgressInfo("Finished moving {0} manga to '{1}'", mangaToMove.Count, destinationRootFolder);
        }
    }
}
