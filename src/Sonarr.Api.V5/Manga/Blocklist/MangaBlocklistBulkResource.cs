namespace Sonarr.Api.V5.Manga.Blocklist
{
    // Sonarr divergence: NEW manga V5 resource per Phase 6 Plan 06-09 — see DIVERGENCE.md.
    // Role-match analog: src/Sonarr.Api.V5/Blocklist/BlocklistBulkResource.cs.
    //
    // Bulk-delete request body for DELETE /api/v5/manga/blocklist/bulk.
    // Phase 8 cleanup: collapse with BlocklistBulkResource when Tv/ deletes.
    public class MangaBlocklistBulkResource
    {
        public List<int> Ids { get; set; } = new List<int>();
    }
}
