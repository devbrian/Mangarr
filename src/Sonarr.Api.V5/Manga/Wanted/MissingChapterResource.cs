using Sonarr.Api.V5.Manga.Subresources;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Manga.Wanted
{
    // Sonarr divergence: NEW manga V5 resource per Phase 6 Plan 06-09 — see DIVERGENCE.md.
    // Role-match analog: src/Sonarr.Api.V5/Episodes/EpisodeResource.cs (consumed by
    // src/Sonarr.Api.V5/Wanted/MissingController.cs).
    //
    // Manga-shaped chapter row for the Wanted/Missing list (WANTED-01..03).
    //
    // Manga sibling preserves: PascalCase POCO + RestResource Id + ToResource() static mapper.
    //
    // Manga sibling diverges from EpisodeResource:
    //   * No SeasonNumber / EpisodeNumber / AirDateUtc / AbsoluteEpisodeNumber (manga has no
    //     season concept; ChapterNumber is decimal; ReleaseDate is DateTime?).
    //   * Adds TranslatedLanguage + ScanlationGroup + IsSynthetic (D-04 — synthetic rows
    //     surface alongside real rows by default; the React layer can opt-in filter via
    //     a future query param).
    //   * Adds optional Manga subresource (lazy hydration via includeManga query).
    //
    // Phase 8 cleanup: collapse with EpisodeResource when Tv/ deletes.
    public class MissingChapterResource : RestResource
    {
        public int MangaId { get; set; }
        public decimal ChapterNumber { get; set; }
        public decimal? AbsoluteChapterNumber { get; set; }
        public int? VolumeNumber { get; set; }
        public string? Title { get; set; }
        public string? TranslatedLanguage { get; set; }
        public string? ScanlationGroup { get; set; }
        public bool IsSynthetic { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public bool Monitored { get; set; }
        public int? ChapterFileId { get; set; }
        public MangaSubresource? Manga { get; set; }
    }

    public static class MissingChapterResourceMapper
    {
        public static MissingChapterResource? ToMissingResource(this NzbDrone.Core.Manga.Chapter? model)
        {
            if (model == null)
            {
                return null;
            }

            return new MissingChapterResource
            {
                Id = model.Id,
                MangaId = model.MangaId,
                ChapterNumber = model.ChapterNumber,
                AbsoluteChapterNumber = model.AbsoluteChapterNumber,
                VolumeNumber = model.VolumeNumber,
                Title = model.Title,
                TranslatedLanguage = model.TranslatedLanguage,
                ScanlationGroup = model.ScanlationGroup,
                IsSynthetic = model.IsSynthetic,
                ReleaseDate = model.ReleaseDate,
                Monitored = model.Monitored,
                ChapterFileId = model.ChapterFileId
            };
        }
    }
}
