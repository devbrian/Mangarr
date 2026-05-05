using System.Collections.Generic;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MediaFiles.Commands
{
    public class RenameMangaCommand : Command
    {
        public List<int> MangaIds { get; set; }

        public override bool SendUpdatesToClient => true;
        public override bool RequiresDiskAccess => true;

        public RenameMangaCommand()
        {
            MangaIds = new List<int>();
        }
    }
}
