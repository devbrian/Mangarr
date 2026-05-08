using NzbDrone.Common.Exceptions;

namespace NzbDrone.Core.MediaFiles
{
    // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — relocated from
    // deleted MediaFiles/EpisodeImport/ namespace.
    public class RecycleBinException : NzbDroneException
    {
        public RecycleBinException(string message)
            : base(message)
        {
        }

        public RecycleBinException(string message, params object[] args)
            : base(message, args)
        {
        }
    }
}
