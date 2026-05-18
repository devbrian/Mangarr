using NzbDrone.Core.Profiles.Delay;

namespace Mangarr.Api.V5.Profiles.Delay;

// Phase 23 Plan 23-02 — Static-extension mapper for DelayProfileResource.
// MIRRORS the static-extension shape from
// `src/Mangarr.Api.V5/Profiles/Release/ReleaseProfileResource.cs` lines 19-59
// (RestrictionResourceMapper), but lives in its own file per Mangarr's
// TagResource.cs / TagResourceMapper.cs split-mapper precedent.
//
// Phase-23 bridge pattern (per CONTEXT.md D-03 + RESEARCH.md Pitfall 2):
// The 4 user-locked-dead entity fields are OMITTED from the V5 Resource
// (DelayProfileResource.cs) but the DelayProfile entity keeps them through Phase 23
// (D-01 — entity-side trim deferred to Phase 26 atomic with Migration 002 DDL
// drop; dropping props in Phase 23 would break the seeder's _repo.Insert(profile)
// against NOT-NULL columns — Pitfall 3).
//
// ToModel() hard-codes those 4 dead-field defaults so the entity round-trips
// cleanly. Each hardcoded line carries a marker comment ABOVE it: the Phase 26
// plan-author greps for the marker token (see the 4 marker lines below in
// ToModel) to find and delete all 4 hardcodes atomic with the entity prop drop
// + Migration 002 column drop.
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

        return new DelayProfile
        {
            Id = resource.Id,

            // PHASE-23 BRIDGE — DELETE atomic with Phase 26 Migration 002 entity prop drop.
            EnableUsenet = true,

            // PHASE-23 BRIDGE — DELETE atomic with Phase 26 Migration 002 entity prop drop.
            EnableTorrent = true,

            // PHASE-23 BRIDGE — DELETE atomic with Phase 26 Migration 002 entity prop drop.
            UsenetDelay = 0,

            // PHASE-23 BRIDGE — DELETE atomic with Phase 26 Migration 002 entity prop drop.
            TorrentDelay = 0,
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
