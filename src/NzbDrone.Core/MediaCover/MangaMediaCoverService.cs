using System;
using System.Collections.Generic;
using System.IO;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.MediaCover
{
    /// <summary>
    /// Maps remote manga cover URLs to local-cache URLs and resolves the on-disk path
    /// for a given manga's cover. Sibling of <see cref="IMapCoversToLocal"/> per
    /// RESEARCH §Pattern 5 — separate interface and folder so manga cover handling
    /// does not collide with series cover handling at the path or DI level.
    /// </summary>
    public interface IMapMangaCoversToLocal
    {
        void ConvertToLocalUrls(int mangaId, IEnumerable<MediaCover> covers);
        string GetMangaCoverPath(int mangaId, MediaCoverTypes coverType, int? height = null);
    }

    /// <summary>
    /// NEW SIBLING of <see cref="MediaCoverService"/> per RESEARCH §Pattern 5. Subscribes to
    /// <see cref="MangaUpdatedEvent"/> and <see cref="MangaDeletedEvent"/>; stores covers under
    /// <c>&lt;data&gt;/MediaCover/manga/{mangaId}/</c> — separate sub-folder from
    /// <see cref="MediaCoverService"/>'s series tree so the two services never collide on
    /// path generation.
    ///
    /// <para>
    /// PER PLAN 02-09 INVARIANT: This file MUST NOT modify <c>MediaCoverService.cs</c> —
    /// the existing series-side service stays untouched. Any rename happens only at Phase 8
    /// cutover.
    /// </para>
    /// </summary>
    public class MangaMediaCoverService :
        IHandleAsync<MangaUpdatedEvent>,
        IHandleAsync<MangaDeletedEvent>,
        IMapMangaCoversToLocal
    {
        private readonly IMediaCoverProxy _mediaCoverProxy;
        private readonly IImageResizer _resizer;
        private readonly IHttpClient _httpClient;
        private readonly IDiskProvider _diskProvider;
        private readonly ICoverExistsSpecification _coverExistsSpecification;
        private readonly Logger _logger;

        // Distinct sub-folder per RESEARCH §Pattern 5 — never collides with the series
        // covers managed by MediaCoverService at <data>/MediaCover/{seriesId}/.
        private readonly string _coverRootFolder;

        public MangaMediaCoverService(IMediaCoverProxy mediaCoverProxy,
                                      IImageResizer resizer,
                                      IHttpClient httpClient,
                                      IDiskProvider diskProvider,
                                      ICoverExistsSpecification coverExistsSpecification,
                                      IAppFolderInfo appFolderInfo,
                                      Logger logger)
        {
            _mediaCoverProxy = mediaCoverProxy;
            _resizer = resizer;
            _httpClient = httpClient;
            _diskProvider = diskProvider;
            _coverExistsSpecification = coverExistsSpecification;
            _logger = logger;
            _coverRootFolder = Path.Combine(appFolderInfo.GetMediaCoverPath(), "manga");
        }

        public string GetMangaCoverPath(int mangaId, MediaCoverTypes coverType, int? height = null)
        {
            var heightSuffix = height.HasValue ? "-" + height.Value : string.Empty;
            return Path.Combine(_coverRootFolder, mangaId.ToString(),
                                $"{coverType.ToString().ToLowerInvariant()}{heightSuffix}{GetExtension(coverType)}");
        }

        public void ConvertToLocalUrls(int mangaId, IEnumerable<MediaCover> covers)
        {
            if (covers == null)
            {
                return;
            }

            foreach (var c in covers)
            {
                if (string.IsNullOrEmpty(c.RemoteUrl) && c.Url != null)
                {
                    c.RemoteUrl = c.Url;
                }

                c.Url = _mediaCoverProxy.RegisterUrl(c.RemoteUrl);
            }
        }

        public void HandleAsync(MangaUpdatedEvent message)
        {
            var manga = message.Manga;
            EnsureCoversFolder(manga.Id);

            foreach (var cover in manga.Images ?? new List<MediaCover>())
            {
                try
                {
                    var localPath = GetMangaCoverPath(manga.Id, cover.CoverType);
                    if (_coverExistsSpecification.AlreadyExists(cover.RemoteUrl, localPath))
                    {
                        continue;
                    }

                    DownloadCover(localPath, cover.RemoteUrl);
                    EnsureResized(localPath, manga.Id, cover.CoverType);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Cover download failed for manga {0} cover {1}", manga.Id, cover.CoverType);
                }
            }
        }

        public void HandleAsync(MangaDeletedEvent message)
        {
            try
            {
                var folder = Path.Combine(_coverRootFolder, message.Manga.Id.ToString());
                if (_diskProvider.FolderExists(folder))
                {
                    _diskProvider.DeleteFolder(folder, recursive: true);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to delete cover folder for manga {0}", message.Manga.Id);
            }
        }

        private void EnsureCoversFolder(int mangaId)
        {
            var folder = Path.Combine(_coverRootFolder, mangaId.ToString());
            if (!_diskProvider.FolderExists(folder))
            {
                _diskProvider.CreateFolder(folder);
            }
        }

        private void DownloadCover(string localPath, string remoteUrl)
        {
            _logger.Info("Downloading manga cover {0}", remoteUrl);
            _httpClient.DownloadFile(remoteUrl, localPath);
        }

        private void EnsureResized(string localPath, int mangaId, MediaCoverTypes coverType)
        {
            // Heights mirror the precedent set in MediaCoverService.EnsureResizedCovers.
            var heights = coverType switch
            {
                MediaCoverTypes.Poster => new[] { 250, 500 },
                MediaCoverTypes.Banner => new[] { 70, 110 },
                MediaCoverTypes.Fanart => new[] { 360, 720 },
                _ => Array.Empty<int>(),
            };

            foreach (var h in heights)
            {
                var resizedPath = GetMangaCoverPath(mangaId, coverType, h);
                if (!_diskProvider.FileExists(resizedPath))
                {
                    try
                    {
                        _resizer.Resize(localPath, resizedPath, h);
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Couldn't resize manga cover {0}-{1} for manga {2}", coverType, h, mangaId);
                    }
                }
            }
        }

        private static string GetExtension(MediaCoverTypes coverType) => ".jpg";
    }
}
