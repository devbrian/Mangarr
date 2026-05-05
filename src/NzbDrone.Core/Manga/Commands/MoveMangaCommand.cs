using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Manga.Commands
{
    public class MoveMangaCommand : Command
    {
        public int MangaId { get; set; }
        public string SourcePath { get; set; }
        public string DestinationPath { get; set; }

        public override bool SendUpdatesToClient => true;
        public override bool RequiresDiskAccess => true;
    }
}
