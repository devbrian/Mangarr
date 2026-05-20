using NzbDrone.Core.Profiles.Delay;

namespace Mangarr.Api.V5.Profiles.Delay;

// Phase 23 Plan 23-02 — Static-extension mapper for DelayProfileResource.
// MIRRORS the static-extension shape from
// `src/Mangarr.Api.V5/Profiles/Release/ReleaseProfileResource.cs` lines 19-59
// (RestrictionResourceMapper), but lives in its own file per Mangarr's
// TagResource.cs / TagResourceMapper.cs split-mapper precedent.
//
// Phase 26 Plan 26-03 (DP-02) closed the Phase 23 bridge pattern: the 4 dead
// protocol-delay entity props were dropped from DelayProfile.cs atomic with
// Migration 003's DDL column drops on DelayProfiles. The previous marker
// hardcodes in ToModel are gone.
public static class DelayProfileResourceMapper
{
    public static DelayProfileResource? ToResource(this DelayProfile model)
    {
        if (model == null)
        {
            return null;
        }

        return new DelayProfileResource
        {
            Id = model.Id,
            PreferredProtocol = model.PreferredProtocol,      // D-04 preserved (dormant)
            HttpDelay = model.HttpDelay,
            Order = model.Order,
            BypassIfHighestQuality = model.BypassIfHighestQuality,
            BypassIfAboveCustomFormatScore = model.BypassIfAboveCustomFormatScore,
            MinimumCustomFormatScore = model.MinimumCustomFormatScore,
            Tags = model.Tags ?? new HashSet<int>()
        };
    }

    public static DelayProfile? ToModel(this DelayProfileResource resource)
    {
        if (resource == null)
        {
            return null;
        }

        // Phase 26 Plan 26-03 (DP-02) — the 4 Phase 23 bridge hardcodes (4 dead
        // protocol-delay fields = ...) were deleted atomic with the entity prop
        // drop + Migration 003 DDL (see Pitfall 1 atomic cluster). The bridge
        // served its single purpose: keeping the seeder's _repo.Insert(profile)
        // from hitting NOT-NULL DDL columns the V5 Resource had already stopped
        // projecting. With Migration 003 dropping those columns and DP-02
        // dropping the entity props, the bridge is no longer reachable from any
        // code path.
        return new DelayProfile
        {
            Id = resource.Id,
            PreferredProtocol = resource.PreferredProtocol,   // D-04 preserved (dormant)
            HttpDelay = resource.HttpDelay,
            Order = resource.Order,
            BypassIfHighestQuality = resource.BypassIfHighestQuality,
            BypassIfAboveCustomFormatScore = resource.BypassIfAboveCustomFormatScore,
            MinimumCustomFormatScore = resource.MinimumCustomFormatScore,
            Tags = resource.Tags ?? new HashSet<int>()
        };
    }

    public static List<DelayProfileResource> ToResource(this IEnumerable<DelayProfile> models)
    {
        return models?.Select(m => m.ToResource()!).Where(r => r != null).ToList() ?? new List<DelayProfileResource>();
    }
}
