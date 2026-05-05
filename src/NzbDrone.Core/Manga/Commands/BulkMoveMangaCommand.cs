using System;
using System.Collections.Generic;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Manga.Commands
{
    public class BulkMoveMangaCommand : Command
    {
        public List<BulkMoveManga> Manga { get; set; }
        public string DestinationRootFolder { get; set; }

        public override bool SendUpdatesToClient => true;
        public override bool RequiresDiskAccess => true;
    }

    public class BulkMoveManga : IEquatable<BulkMoveManga>
    {
        public int MangaId { get; set; }
        public string SourcePath { get; set; }

        public bool Equals(BulkMoveManga other)
        {
            if (other == null)
            {
                return false;
            }

            return MangaId.Equals(other.MangaId);
        }

        public override bool Equals(object obj)
        {
            if (obj == null)
            {
                return false;
            }

            if (obj.GetType() != GetType())
            {
                return false;
            }

            return MangaId.Equals(((BulkMoveManga)obj).MangaId);
        }

        public override int GetHashCode()
        {
            return MangaId.GetHashCode();
        }
    }
}
