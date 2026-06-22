using NzbDrone.Core.Manga;

namespace Mangarr.Api.V5.Discovery;

// Phase 42 Plan 42-04 (DISC-07) — the POST /api/v5/discovery/bulk-add request payload.
//
// STRICT 7-field allow-list (T-42-04-MASS mass-assignment mitigation, MangaEditorResource
// precedent): System.Text.Json silently drops any field outside this set, so an attacker
// cannot smuggle extra Manga fields through the bulk-add boundary. These exactly mirror the
// add-options DiscoveryBulkAddCommand carries (42-03); the controller maps them 1:1 onto the
// command and clamps MangaBakaIds to 100 before enqueue (T-42-04-DOS).
public class DiscoveryBulkAddResource
{
    public List<int> MangaBakaIds { get; set; } = [];
    public string? RootFolderPath { get; set; }
    public MangaMonitor Monitor { get; set; }
    public int TranslationProfileId { get; set; }
    public int CustomFormatProfileId { get; set; }
    public List<int> Tags { get; set; } = [];
    public bool SearchForMissingChapters { get; set; }
}
