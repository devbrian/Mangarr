using System.Linq;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Parser.Manga;

namespace NzbDrone.Core.Housekeeping.Housekeepers
{
    // Manga-side parity for TV Housekeepers/UpdateCleanTitleForSeries.cs.
    // Recomputes CleanTitle for every manga whose stored value has drifted from
    // the canonical MangaTitleNormalizer output (Phase 2 D-05). Mirrors the TV
    // housekeeper shape verbatim — IHousekeepingTask, repo enumerate, update on
    // diff — diverging only on the title-cleaning helper (manga uses the
    // canonical normalizer, not the legacy CleanSeriesTitle extension).
    public class UpdateCleanTitleForManga : IHousekeepingTask
    {
        private readonly IMangaRepository _mangaRepository;

        public UpdateCleanTitleForManga(IMangaRepository mangaRepository)
        {
            _mangaRepository = mangaRepository;
        }

        public void Clean()
        {
            var manga = _mangaRepository.All().ToList();

            manga.ForEach(m =>
            {
                var cleanTitle = MangaTitleNormalizer.Normalize(m.Title);
                if (m.CleanTitle != cleanTitle)
                {
                    m.CleanTitle = cleanTitle;
                    _mangaRepository.Update(m);
                }
            });
        }
    }
}
