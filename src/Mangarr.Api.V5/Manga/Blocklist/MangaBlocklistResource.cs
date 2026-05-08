using NzbDrone.Core.Blocklisting.Manga;
using Mangarr.Api.V5.Manga.Subresources;
using Mangarr.Http.REST;

namespace Mangarr.Api.V5.Manga.Blocklist
{
    // Sonarr divergence: NEW manga V5 resource per Phase 6 Plan 06-09 — see DIVERGENCE.md.
    // Role-match analog: src/Mangarr.Api.V5/Blocklist/BlocklistResource.cs.
    //
    // Manga sibling preserves: PascalCase POCO + RestResource Id + ToResource() static mapper.
    // Manga sibling diverges:
    //   * No Quality / Languages / Protocol / TorrentInfoHash (manga has no quality model;
    //     v1 only DownloadProtocol.Http; D-11 release identity = (SourceKey, ReleaseGuid, Title)
    //     triple).
    //   * Adds ChapterIds : List<int> (the set of chapters this blocklist row covers).
    //
    // Phase 8 cleanup: collapse with BlocklistResource when Tv/ deletes.
    public class MangaBlocklistResource : RestResource
    {
        public int MangaId { get; set; }
        public List<int>? ChapterIds { get; set; }
        public string? SourceTitle { get; set; }
        public string? SourceKey { get; set; }
        public string? ReleaseGuid { get; set; }
        public DateTime Date { get; set; }
        public string? Reason { get; set; }
        public string? Source { get; set; }
        public MangaSubresource? Manga { get; set; }
    }

    public static class MangaBlocklistResourceMapper
    {
        public static MangaBlocklistResource? ToResource(this MangaBlocklist? model)
        {
            if (model == null)
            {
                return null;
            }

            return new MangaBlocklistResource
            {
                Id = model.Id,
                MangaId = model.MangaId,
                ChapterIds = model.ChapterIds,
                SourceTitle = model.SourceTitle,
                SourceKey = model.SourceKey,
                ReleaseGuid = model.ReleaseGuid,
                Date = model.Date,
                Reason = model.Reason,
                Source = model.Source
            };
        }
    }
}
