using Mangarr.Http.ClientSchema;
using Mangarr.Http.REST;

namespace Mangarr.Api.V5.AutoTagging;

// Phase 24 Plan 24-04 — V5 AutoTaggingSpecificationResource (a.k.a. SchemaResource).
// Port from Sonarr v5-develop's AutoTaggingSpecificationSchema.cs at the pinned SHA
// dfb157382b20a2d4eb5f5828a6c1e276c0d6b160. Plan-author file-name choice:
// "AutoTaggingSpecificationResource" matches the plan frontmatter's <files> entry
// (24-04-PLAN.md line 13); Sonarr's source spelling is "AutoTaggingSpecificationSchema"
// (functionally identical — both are a RestResource subclass carrying schema rows).
//
// FE consumer: frontend/src/typings/AutoTagging.ts lines 4-12 — Name / Implementation /
// ImplementationName / Negate / Required / Fields[] — round-trip target.
//
// Fields is a List<Field> from Mangarr.Http.ClientSchema — reuse the existing
// SchemaBuilder reflection pipeline (already used by every other provider schema
// endpoint in Mangarr.Api.V5: Indexers, DownloadClients, ImportLists, Notifications).
public class AutoTaggingSpecificationResource : RestResource
{
    public string? Name { get; set; }
    public string? Implementation { get; set; }
    public string? ImplementationName { get; set; }
    public bool Negate { get; set; }
    public bool Required { get; set; }
    public List<Field> Fields { get; set; } = new();
}
