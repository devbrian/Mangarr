using NzbDrone.Common.Exceptions;

namespace NzbDrone.Core.MediaFiles
{
    // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — relocated from
    // deleted MediaFiles/EpisodeImport/ namespace. Manga import code references this
    // exception by simple name (no using fix needed since RecycleBinProvider /
    // ChapterFileMovingService / ImportApprovedChapters live in MediaFiles namespace).
    public class RootFolderNotFoundException : NzbDroneException
    {
        public RootFolderNotFoundException(string message)
            : base(message)
        {
        }

        public RootFolderNotFoundException(string message, params object[] args)
            : base(message, args)
        {
        }
    }
}
