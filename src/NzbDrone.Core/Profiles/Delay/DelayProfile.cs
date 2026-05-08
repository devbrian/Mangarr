using System.Collections.Generic;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Indexers;

namespace NzbDrone.Core.Profiles.Delay
{
    public class DelayProfile : ModelBase
    {
        public bool EnableUsenet { get; set; }
        public bool EnableTorrent { get; set; }
        public DownloadProtocol PreferredProtocol { get; set; }
        public int UsenetDelay { get; set; }
        public int TorrentDelay { get; set; }
        public int HttpDelay { get; set; }
        public int Order { get; set; }
        public bool BypassIfHighestQuality { get; set; }
        public bool BypassIfAboveCustomFormatScore { get; set; }
        public int MinimumCustomFormatScore { get; set; }
        public HashSet<int> Tags { get; set; }

        public DelayProfile()
        {
            Tags = new HashSet<int>();
        }

        // Sonarr divergence: Phase 15 Plan 15-04 — DownloadProtocol.{Usenet,Torrent} stripped per
        // Plan 15-04 enum trim (Discretion lean Unknown=0, Http=3). HttpDelay is the only active
        // protocol delay; Torrent/Usenet delays remain as schema-round-trip columns.
        public int GetProtocolDelay(DownloadProtocol protocol)
        {
            return protocol == DownloadProtocol.Http ? HttpDelay : UsenetDelay;
        }
    }
}
