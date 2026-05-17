using Mangarr.Http.REST;
using NzbDrone.Core.Indexers;

namespace Mangarr.Api.V5.Profiles.Delay;

// Phase 23 Plan 23-02 — V5 Resource for DelayProfile. CLEAN V5 SURFACE per D-03:
// OMITS the 4 user-locked dead fields that were killed when Phase 15 D-18 trimmed
// DownloadProtocol's Usenet/Torrent enum values. PreferredProtocol is PRESERVED
// per D-04 + D-12 (dormant, not dead — forward-compatible with future
// Direct/Scraper protocols).
//
// Sonarr V3 source comparison (pinned at 23-02-PORT-SOURCE.md Sonarr SHA
// dfb157382b20a2d4eb5f5828a6c1e276c0d6b160) showed Resource has NO Name property
// (A1 = NO); Mangarr port matches.
//
// The 4 omitted fields are bridged on the entity side by DelayProfileResourceMapper
// (hardcoded defaults, each marked for Phase 26 cleanup — see the mapper's marker
// comments). NO [JsonIgnore] decorations on this Resource — the type stays clean;
// the mapper carries the side-channel.
public class DelayProfileResource : RestResource
{
    public DownloadProtocol PreferredProtocol { get; set; }    // D-04 preserved (dormant)
    public int HttpDelay { get; set; }                          // Mangarr canonical active delay
    public int Order { get; set; }
    public bool BypassIfHighestQuality { get; set; }
    public bool BypassIfAboveCustomFormatScore { get; set; }
    public int MinimumCustomFormatScore { get; set; }
    public HashSet<int> Tags { get; set; } = new();
}
