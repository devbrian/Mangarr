namespace Mangarr.Api.V5.ImportLists;

// Phase 26 Plan 26-05 (IL-05) — ported verbatim from
// .planning/reference/sonarr-vertical-slices/import-lists/v5-controller/ImportListExclusionBulkResource.cs.
// Bulk-payload shape is ID-only; no triplet fields needed (the IDs are the
// primary-key references to ImportListExclusion rows).
public class ImportListExclusionBulkResource
{
    public required HashSet<int> Ids { get; set; }
}
