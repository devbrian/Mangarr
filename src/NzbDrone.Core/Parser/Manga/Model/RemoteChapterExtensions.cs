using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Parser.Model;
using TvEpisode = NzbDrone.Core.Tv.Episode;
using TvSeries = NzbDrone.Core.Tv.Series;

namespace NzbDrone.Core.Parser.Manga.Model
{
    public static class RemoteChapterExtensions
    {
        // Wire-level shim: TV's IDownloadService takes RemoteEpisode and the IDownloadClient
        // contract is `Download(RemoteEpisode, IIndexer)`. Phase 4 D-10 routes Http-protocol
        // releases past TV's import path; the in-process client only reads `.Release`,
        // `.Series.Id`, and `.Episodes[0].Id`. Phase 15 collapse renames the type and removes
        // this shim along with the IProcessMangaDownloadDecisions / MangaReleaseController
        // call sites.
        public static RemoteEpisode ToRemoteEpisodeShim(this RemoteChapter remoteChapter)
        {
            var manga = remoteChapter.Manga;
            var chapters = remoteChapter.Chapters ?? new List<NzbDrone.Core.Manga.Chapter>();

            var seriesShim = new TvSeries { Id = manga?.Id ?? 0 };
            var episodeShims = chapters
                .Select(c => new TvEpisode { Id = c.Id })
                .ToList();
            if (episodeShims.Count == 0)
            {
                episodeShims.Add(new TvEpisode { Id = 0 });
            }

            return new RemoteEpisode
            {
                Release = remoteChapter.Release,
                Series = seriesShim,
                Episodes = episodeShims,
                CustomFormats = remoteChapter.CustomFormats,
                CustomFormatScore = remoteChapter.CustomFormatScore
            };
        }
    }
}
