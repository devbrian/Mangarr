using System.Collections.Generic;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Indexers;

namespace NzbDrone.Core.Profiles.Delay
{
    public class DelayProfile : ModelBase
    {
        public DownloadProtocol PreferredProtocol { get; set; }
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

        // Phase 26 Plan 26-03 (DP-02) — body simplified to HttpDelay-only after
        // Migration 003 drops the 4 dead protocol-delay columns. Post-Phase-15 D-18
        // enum trim only Http=3 and Unknown=0 remain on DownloadProtocol; any
        // non-Http branch falls through to 0.
        public int GetProtocolDelay(DownloadProtocol protocol)
        {
            return protocol switch
            {
                DownloadProtocol.Http => HttpDelay,
                _ => 0
            };
        }
    }
}
