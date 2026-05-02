using System.Linq;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Manga.Events;

namespace NzbDrone.Core.HealthCheck.Checks
{
    /// <summary>
    /// Per CONTEXT Claude's Discretion: fires Warning when any Manga has zero Chapter rows
    /// AND the linked primary returned a non-null total chapter count (i.e. D-17 synthesis
    /// strategies 1+2 should have populated rows but didn't).
    ///
    /// <para>
    /// CheckOn triggers re-evaluation whenever <see cref="MangaUpdatedEvent"/> or
    /// <see cref="ChapterListUpdatedEvent"/> fires — keeping the surface up to date
    /// without polling.
    /// </para>
    /// </summary>
    [CheckOn(typeof(MangaUpdatedEvent))]
    [CheckOn(typeof(ChapterListUpdatedEvent))]
    public class MissingChapterListHealthCheck : HealthCheckBase
    {
        private readonly IMangaService _mangaService;
        private readonly IChapterRepository _chapterRepository;

        public MissingChapterListHealthCheck(IMangaService mangaService,
                                             IChapterRepository chapterRepository,
                                             ILocalizationService localizationService)
            : base(localizationService)
        {
            _mangaService = mangaService;
            _chapterRepository = chapterRepository;
        }

        public override HealthCheck Check()
        {
            // Per CONTEXT Claude's Discretion: fires when Manga has zero Chapter rows AND
            // the linked primary returned non-null chapter-count (synthesis would have populated).
            var problemManga = _mangaService.GetAllManga()
                .Where(m => m.TotalChapterCount.HasValue && m.TotalChapterCount.Value > 0)
                .Where(m => !_chapterRepository.GetByMangaId(m.Id).Any())
                .ToList();

            if (problemManga.Count == 0)
            {
                return new HealthCheck(GetType());
            }

            var titles = string.Join(", ", problemManga.Take(5).Select(m => m.Title));
            var more = problemManga.Count > 5 ? $" (+{problemManga.Count - 5} more)" : string.Empty;

            return new HealthCheck(GetType(),
                HealthCheckResult.Warning,
                HealthCheckReason.MissingChapterList,
                $"Chapter list synthesis failed for: {titles}{more}",
                "#missing-chapter-list");
        }
    }
}
