using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Test.Download.Manga.Builders
{
    // Sonarr divergence: NEW Phase 36 shared test infrastructure (Wave 0 gap) — see DIVERGENCE.md.
    // No production analog: this is a test-only fluent builder reused by the Plan 02/03/04 fixtures.
    //
    // Constructs a TrackedDownload with a populated RemoteChapter (the canonical manga projection
    // slot — TrackedDownload.cs:17, never RemoteEpisode which was stripped Phase 15), a DownloadItem
    // delegated to DownloadClientItemBuilder, and the kept TrackedDownloadState/Status enums and
    // Fail() (TrackedDownload.cs:43-70 — reuse, never redefine). Keep minimal, deterministic, and
    // free of ChapterDownloadState coupling (gateway-path-clean).
    public class TrackedDownloadBuilder
    {
        private readonly TrackedDownload _trackedDownload;
        private readonly NzbDrone.Core.Manga.Manga _manga;
        private readonly ReleaseInfo _release;
        private readonly DownloadClientItemBuilder _itemBuilder;
        private List<NzbDrone.Core.Manga.Chapter> _chapters;

        public TrackedDownloadBuilder()
        {
            _manga = new NzbDrone.Core.Manga.Manga { Id = 7, Title = "Test Manga" };
            _chapters = new List<NzbDrone.Core.Manga.Chapter>
            {
                new() { Id = 42, MangaId = 7, ChapterNumber = 1m }
            };
            _release = new ReleaseInfo
            {
                Title = "Test Manga - Chapter 001",
                Indexer = "MangaDex",
                Guid = "guid-tracked-1",
                TranslatedLanguage = "en",
                DownloadProtocol = DownloadProtocol.Http
            };
            _itemBuilder = new DownloadClientItemBuilder();

            _trackedDownload = new TrackedDownload
            {
                Protocol = DownloadProtocol.Http,
                Indexer = "MangaDex",
                State = TrackedDownloadState.Downloading
            };
        }

        public TrackedDownloadBuilder WithDownloadId(string downloadId)
        {
            _itemBuilder.WithDownloadId(downloadId);
            return this;
        }

        public TrackedDownloadBuilder WithChapters(params int[] chapterIds)
        {
            _chapters = chapterIds
                .Select(id => new NzbDrone.Core.Manga.Chapter { Id = id, MangaId = _manga.Id, ChapterNumber = id })
                .ToList();
            return this;
        }

        public TrackedDownloadBuilder WithLanguage(string translatedLanguage)
        {
            _release.TranslatedLanguage = translatedLanguage;
            return this;
        }

        public TrackedDownloadBuilder WithOutputPath(string path)
        {
            _itemBuilder.WithOutputPath(path);
            return this;
        }

        public TrackedDownloadBuilder InState(TrackedDownloadState state)
        {
            _trackedDownload.State = state;
            return this;
        }

        public TrackedDownloadBuilder Completed()
        {
            _itemBuilder.Completed();
            _trackedDownload.State = TrackedDownloadState.ImportPending;
            return this;
        }

        public TrackedDownloadBuilder Failed()
        {
            _itemBuilder.Failed();
            return this;
        }

        public TrackedDownload Build()
        {
            _trackedDownload.RemoteChapter = new RemoteChapter
            {
                Manga = _manga,
                Chapters = _chapters,
                Release = _release
            };

            var item = _itemBuilder.Build();
            _trackedDownload.DownloadItem = item;

            // Failed() on the builder maps to the kept TrackedDownload.Fail() (sets Error +
            // FailedPending + CanBeRemoved) so consumers see the canonical failed shape.
            if (item.Status == DownloadItemStatus.Failed)
            {
                _trackedDownload.Fail();
            }

            return _trackedDownload;
        }
    }
}
