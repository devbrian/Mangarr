// Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — relocated from
// deleted Mangarr.Api.V5/Queue/QueueBulkResource.cs (TV V5/Queue dir DELETED).
// Domain-neutral { Ids: int[] } payload reused by manga queue bulk endpoints.
namespace Mangarr.Api.V5.Manga.Queue;

public class QueueBulkResource
{
    public required List<int> Ids { get; set; }
}
