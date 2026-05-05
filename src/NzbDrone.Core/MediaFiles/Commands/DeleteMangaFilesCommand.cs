using System.Collections.Generic;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MediaFiles.Commands
{
    public class DeleteMangaFilesCommand : Command
    {
        public List<int> MangaIds { get; set; }

        public override bool SendUpdatesToClient => true;
        public override bool RequiresDiskAccess => true;

        public DeleteMangaFilesCommand()
        {
            MangaIds = new List<int>();
        }
    }
}
