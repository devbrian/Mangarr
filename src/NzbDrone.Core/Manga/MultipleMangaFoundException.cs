using System.Collections.Generic;
using NzbDrone.Common.Exceptions;

namespace NzbDrone.Core.Manga
{
    // Phase 8 audit gap-04 (SeriesRepository-vs-MangaRepository.md): mirrors
    // Tv/MultipleSeriesFoundException.cs verbatim. Carries the matched-list to the
    // caller so that disambiguation (e.g., year-overload re-query, manual UI prompt)
    // can be performed instead of swallowing the InvalidOperationException that
    // .SingleOrDefault() would otherwise raise on multi-match.
    public class MultipleMangaFoundException : NzbDroneException
    {
        public List<Manga> Manga { get; set; }

        public MultipleMangaFoundException(List<Manga> manga, string message, params object[] args)
            : base(message, args)
        {
            Manga = manga;
        }
    }
}
