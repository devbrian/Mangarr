using Sonarr.Api.V5.Manga.Subresources;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Manga.Wanted
{
    // Sonarr divergence: NEW manga V5 resource per Phase 12 Plan 12-12 (sub-wave-B-addition #4 —
    // F-CUTOFF closure). See DIVERGENCE.md.
    // Role-match analog: src/Sonarr.Api.V5/Manga/Wanted/MissingChapterResource.cs (Plan 06-09 sibling).
    // TV peer reference: src/Sonarr.Api.V5/Wanted/CutoffController.cs surfaces TV's
    // EpisodeResource via the EpisodeControllerWithSignalR base; the manga peer mirrors the
    // MissingChapterResource shape (the manga V5 Wanted controllers extend plain Controller,
    // not EpisodeControllerWithSignalR, so they ship their own paged-row resource type).
    //
    // Manga-shaped chapter row for the Wanted/Cutoff list (cutoff-unmet feed driven by
    // IChapterCutoffService.ChaptersWhereCutoffUnmet — Phase 8 audit no-sibling/EpisodeCutoffService.md
    // shipped the service; Plan 12-12 ships the V5 controller surface).
    //
    // Manga sibling preserves: PascalCase POCO + RestResource Id + ToResource() static mapper.
    //
    // Manga sibling diverges from MissingChapterResource:
    //   * Distinct type name (MangaCutoffResource vs MissingChapterResource) — same row shape, distinct
    //     filing per CLAUDE.md HIGH-PRIORITY documentation convention. Phase 8 cleanup may merge if
    //     the two converge after the cutover (currently identical fields).
    //
    // Manga sibling diverges from TV's EpisodeResource (used by CutoffController):
    //   * No SeasonNumber / EpisodeNumber / AirDateUtc / AbsoluteEpisodeNumber (manga has no
    //     season concept; ChapterNumber is decimal; ReleaseDate is DateTime?).
    //   * Adds TranslatedLanguage + ScanlationGroup + IsSynthetic (D-04 — synthetic rows
    //     surface alongside real rows by default).
    //   * Adds optional Manga subresource (lazy hydration via includeManga query).
    //
    // Phase 8 cleanup: collapse with MissingChapterResource (same row shape) AND with TV's
    // EpisodeResource when Tv/ deletes.
    public class MangaCutoffResource : RestResource
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

    public static class MangaCutoffResourceMapper
    {
        public static MangaCutoffResource? ToCutoffResource(this NzbDrone.Core.Manga.Chapter? model)
        {
            if (model == null)
            {
                return null;
            }

            return new MangaCutoffResource
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
