using System.Text.Json.Serialization;
using Mangarr.Http.REST;

namespace Mangarr.Api.V5.AutoTagging;

// Phase 24 Plan 24-04 — V5 AutoTaggingResource port from Sonarr v5-develop
// (pinned at SHA dfb157382b20a2d4eb5f5828a6c1e276c0d6b160 per 24-01-PORT-SOURCE.md).
// Sonarr shape preserved verbatim: Id is force-serialized (JsonIgnoreCondition.Never)
// so the FE Redux thunk getter at frontend/src/Store/Actions/Settings/autoTaggings.js
// can key off it even when default. RemoveTagsAutomatically is preserved per D-05
// (Sonarr-canonical opt-in per-rule flag; default false = additive).
//
// Nested Specifications collection uses AutoTaggingSpecificationSchema directly
// — Sonarr V5 has NO separate Resource type for the in-rule spec list; the
// schema-row carries everything the FE needs (per Open Q #3 resolution).
public class AutoTaggingResource : RestResource
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public override int Id { get; set; }
    public string? Name { get; set; }
    public bool RemoveTagsAutomatically { get; set; }
    public HashSet<int> Tags { get; set; } = new();
    public List<AutoTaggingSpecificationResource> Specifications { get; set; } = new();
}
