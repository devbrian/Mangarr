using NzbDrone.Core.History.Manga;
using Sonarr.Api.V5.Manga.Subresources;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Manga.History
{
    // Sonarr divergence: NEW manga V5 resource per Phase 6 Plan 06-09 — see DIVERGENCE.md.
    // Role-match analog: src/Sonarr.Api.V5/History/HistoryResource.cs.
    //
    // Manga sibling preserves: PascalCase POCO + RestResource Id + ToResource() static mapper
    // (Sonarr V5 convention); flattens entity → REST shape (no EF reference leakage).
    //
    // Manga sibling diverges from HistoryResource:
    //   * No QualityModel / Languages enum (manga has no quality model per Phase 5 D-04;
    //     TranslatedLanguage is BCP-47 string per Phase 3 D-Q4).
    //   * Adds ScanlationGroup, SourceKey, ReleaseGuid for D-11 release identity triple.
    //   * Subresources are MangaSubresource + ChapterSubresource (not Series + Episode).
    //
    // Phase 8 cleanup: collapse with HistoryResource when Tv/ deletes.
    public class ChapterHistoryResource : RestResource
    {
        public int MangaId { get; set; }
        public int ChapterId { get; set; }
        public string? SourceTitle { get; set; }
        public DateTime Date { get; set; }

        // Was `string EventType` populated via `EventType.ToString()` — that emits the
        // PascalCase enum name ("Grabbed", "Imported"), but the frontend `HistoryEventType`
        // typings and the `HistoryEventTypeCell` switch both consume camelCase ("grabbed",
        // "imported") through the global StringEnumConverter the rest of the V5 API uses.
        // Mirror TV's `HistoryResource.EventType` shape (direct enum field) so the global
        // serializer applies the camelCase-text setting consistently. Surfaced by
        // quick-260507-tff-rerun2 (history rows showed "Unknown event").
        public ChapterHistoryEventType EventType { get; set; }
        public Dictionary<string, string>? Data { get; set; }
        public string? DownloadId { get; set; }
        public string? TranslatedLanguage { get; set; }
        public string? ScanlationGroup { get; set; }
        public string? SourceKey { get; set; }
        public string? ReleaseGuid { get; set; }
        public bool Successful { get; set; }
        public MangaSubresource? Manga { get; set; }
        public ChapterSubresource? Chapter { get; set; }
    }

    public static class ChapterHistoryResourceMapper
    {
        public static ChapterHistoryResource? ToResource(this ChapterHistory? model)
        {
            if (model == null)
            {
                return null;
            }

            return new ChapterHistoryResource
            {
                Id = model.Id,
                MangaId = model.MangaId,
                ChapterId = model.ChapterId,
                SourceTitle = model.SourceTitle,
                Date = model.Date,
                EventType = model.EventType,
                Data = model.Data,
                DownloadId = model.DownloadId,
                TranslatedLanguage = model.TranslatedLanguage,
                ScanlationGroup = model.ScanlationGroup,
                SourceKey = model.SourceKey,
                ReleaseGuid = model.ReleaseGuid,
                Successful = model.Successful
            };
        }
    }
}
