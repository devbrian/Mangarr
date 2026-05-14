using System;
using System.Collections.Generic;
using System.IO;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.MediaCover
{
    /// <summary>
    /// Maps remote manga cover URLs to local-cache URLs and resolves the on-disk path
    /// for a given manga's cover. Sibling of Mangarr's <c>IMapCoversToLocal</c> (DELETED Phase 15) per
    /// RESEARCH §Pattern 5 — separate interface and folder so manga cover handling
    /// does not collide with series cover handling at the path or DI level.
    /// </summary>
    public interface IMapMangaCoversToLocal
    {
        void ConvertToLocalUrls(int mangaId, IEnumerable<MediaCover> covers);
        string GetMangaCoverPath(int mangaId, MediaCoverTypes coverType, int? height = null);
    }

    /// <summary>
    /// NEW SIBLING of Mangarr's <c>MediaCoverService</c> (DELETED Phase 15) per RESEARCH §Pattern 5. Subscribes to
    /// <see cref="MangaUpdatedEvent"/> and <see cref="MangaDeletedEvent"/>; stores covers under
    /// <c>&lt;data&gt;/MediaCover/manga/{mangaId}/</c> — separate sub-folder from
    /// Mangarr's <c>MediaCoverService</c> (DELETED Phase 15) series tree so the two services never collide on
    /// path generation.
    ///
    /// <para>
    /// PER PLAN 02-09 INVARIANT: This file MUST NOT modify <c>MediaCoverService.cs</c> —
    /// the existing series-side service stays untouched. Any rename happens only at Phase 8
    /// cutover.
    /// </para>
    /// <para>
    /// PHASE 9 PLAN 09-13 (sub-wave A 09-04 audit gap-03 + gap-04 close-out): publishes
    /// <see cref="MangaCoversUpdatedEvent"/> at end of <see cref="HandleAsync(MangaUpdatedEvent)"/>
    /// (consumed by MangaController.Handle for SignalR resource broadcast); rewrites
    /// <c>ConvertToLocalUrls</c> with the saved-manga branch (mirroring
    /// Mangarr's <c>MediaCoverService.ConvertToLocalUrls</c> (DELETED Phase 15):73-103) so saved manga serve covers
    /// from the on-disk cache with <c>?lastWrite={ticks}</c> cache-bust suffix instead of
    /// always proxy-routing. Pitfall 4 ordering preserved: disk writes (DownloadCover +
    /// EnsureResized) FIRST, event publish LAST.
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
        private readonly IEventAggregator _eventAggregator;       // NEW per Plan 09-13 (audit gap-03)
        private readonly IConfigFileProvider _configFileProvider; // NEW per Plan 09-13 (audit gap-04)
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
                                      IEventAggregator eventAggregator,
                                      IConfigFileProvider configFileProvider,
                                      Logger logger)
        {
            _mediaCoverProxy = mediaCoverProxy;
            _resizer = resizer;
            _httpClient = httpClient;
            _diskProvider = diskProvider;
            _coverExistsSpecification = coverExistsSpecification;
            _eventAggregator = eventAggregator;             // NEW per Plan 09-13 (audit gap-03)
            _configFileProvider = configFileProvider;       // NEW per Plan 09-13 (audit gap-04)
            _logger = logger;
            _coverRootFolder = Path.Combine(appFolderInfo.GetMediaCoverPath(), "manga");
        }

        public string GetMangaCoverPath(int mangaId, MediaCoverTypes coverType, int? height = null)
        {
            var heightSuffix = height.HasValue ? "-" + height.Value : string.Empty;
            return Path.Combine(
                _coverRootFolder,
                mangaId.ToString(),
                $"{coverType.ToString().ToLowerInvariant()}{heightSuffix}{GetExtension(coverType)}");
        }

        // PHASE 9 PLAN 09-13 (audit gap-04 close-out): rewritten with proxy-vs-local branch
        // mirroring TV MediaCoverService.cs:73-103. mangaId == 0 routes through proxy
        // (referrer-dodging path for unsaved manga); mangaId != 0 rewrites to a local-cache
        // URL with `?lastWrite={ticks}` cache-bust suffix when the on-disk file exists.
        // The static-file mapper (Mangarr.Http/Frontend/Mappers/MediaCoverMapper.cs:44-47)
        // already serves the /MediaCover/manga/... shape from disk transparently.
        public void ConvertToLocalUrls(int mangaId, IEnumerable<MediaCover> covers)
        {
            if (covers == null)
            {
                return;
            }

            foreach (var c in covers)
            {
                // Defensive guard predating the proxy path — protect against upstream metadata
                // sources that only populate .Url. Preserved per audit gap-04 backfill_notes
                // line 140 ("Preserve this fallback in the rewrite").
                if (string.IsNullOrEmpty(c.RemoteUrl) && c.Url != null)
                {
                    c.RemoteUrl = c.Url;
                }

                if (mangaId == 0)
                {
                    // Manga isn't in the database yet — map via proxy (referrer-dodging path).
                    // Mirrors TV MediaCoverService.cs:78 verbatim shape with seriesId→mangaId.
                    c.Url = _mediaCoverProxy.RegisterUrl(c.RemoteUrl);
                }
                else
                {
                    // Saved manga: rewrite to local-cache URL with ?lastWrite= cache-bust suffix
                    // when the on-disk file exists. Mirrors TV MediaCoverService.cs:83-101 with
                    // /MediaCover/{seriesId}/ → /MediaCover/manga/{mangaId}/ substitution.
                    if (c.CoverType == MediaCoverTypes.Unknown)
                    {
                        continue;
                    }

                    var filePath = GetMangaCoverPath(mangaId, c.CoverType);
                    c.Url = _configFileProvider.UrlBase + "/MediaCover/manga/" + mangaId + "/" +
                            c.CoverType.ToString().ToLowerInvariant() + GetExtension(c.CoverType);

                    if (_diskProvider.FileExists(filePath))
                    {
                        var lastWrite = _diskProvider.FileGetLastWrite(filePath);
                        c.Url += "?lastWrite=" + lastWrite.Ticks;
                    }
                }
            }
        }

        // WR-15 documented limitation: this implementation is synchronous despite
        // the IHandleAsync<> contract — _httpClient.DownloadFile and _resizer.Resize
        // are sync, and EventAggregator dispatches us on the publishing thread.
        // For a 100-manga library refresh this can stall the event-aggregator
        // thread for tens of seconds. Acceptable for Phase 2 (refresh cadence is
        // 12h, blocking time is bounded). DEFERRED to Phase 5+: either move to a
        // background command (mirror Mangarr's EnsureMediaCovers) or wait for
        // IHttpClient to grow real async APIs. Tracked alongside the existing
        // Phase 2 deferred items.
        public void HandleAsync(MangaUpdatedEvent message)
        {
            var manga = message.Manga;

            // WR-16 fix: a failed CreateFolder (read-only volume, full disk,
            // permissions) used to bubble out of HandleAsync, killing all subsequent
            // cover downloads for this event AND any chained handler subscriptions
            // EventAggregator was about to dispatch. Bail this manga gracefully
            // (Warn + return) so other handlers still run.
            //
            // PHASE 9 PLAN 09-13 NOTE: early-return path does NOT publish MangaCoversUpdatedEvent
            // because no covers were attempted. SignalR consumer skips the no-op resource refresh.
            try
            {
                EnsureCoversFolder(manga.Id);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to ensure cover folder for manga {0}; skipping cover sync", manga.Id);
                return;
            }

            // DEFENSE-IN-DEPTH (debug wrong-cover-image-after-add): drop any on-disk cover
            // file whose cover type the CURRENT manga does not have. The `Manga` table has
            // no AUTOINCREMENT, so a reused SQLite rowid can inherit the previous tenant's
            // MediaCover/manga/{id}/ folder. HandleAsync(MangaDeletedEvent) is supposed to
            // wipe that folder, but it is fire-and-forget and can lose the race with this
            // handler — or fail transiently and swallow the error. Pruning orphans here
            // makes a reused Id start from a clean slate regardless of delete-handler
            // timing. The primary fix (EnsureResized regenerate) handles the same-cover-type
            // case; this catches orphaned cover TYPES the new manga no longer carries.
            PruneOrphanedCovers(manga);

            // PHASE 9 PLAN 09-13 (audit gap-03): track whether at least one cover was
            // newly downloaded (AlreadyExists short-circuit leaves it false). The event's
            // `Updated` flag drives MangaController's SignalR push — only fires when a
            // genuine change happened.
            var updated = false;

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

                    // Reaching this line means DownloadCover just rewrote the base file
                    // (AlreadyExists returned false). Force-regenerate the resized variants
                    // from the fresh base file so a reused manga Id cannot keep serving the
                    // previous tenant's poster-{h}.jpg. See debug session
                    // wrong-cover-image-after-add: the `Manga` table has no AUTOINCREMENT,
                    // so SQLite reuses a deleted row's rowid; without this regenerate the
                    // skip-if-exists branch in EnsureResized leaves stale resized covers.
                    EnsureResized(localPath, manga.Id, cover.CoverType, regenerate: true);

                    // Per-cover write succeeded — flag the batch as containing genuine changes.
                    updated = true;
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Cover download failed for manga {0} cover {1}", manga.Id, cover.CoverType);

                    // Per-cover failure does NOT flip `updated` to true (no successful disk write).
                    // Per-cover failure does NOT abort the batch (existing log-and-continue contract).
                }
            }

            // PHASE 9 PLAN 09-13 (audit gap-03) — Pitfall 4 GUARD: publish AFTER all on-disk
            // writes complete. SignalR consumer (MangaController.Handle) race-fires on this
            // event and re-fetches the manga resource via BroadcastResourceChange; if the
            // event published before disk writes, consumer would render stale thumbnails.
            // The existing per-cover try/catch ensures all disk writes that COULD complete
            // HAVE completed by the time we reach this line.
            _eventAggregator.PublishEvent(new MangaCoversUpdatedEvent(manga, updated));
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

        // DEFENSE-IN-DEPTH (debug wrong-cover-image-after-add): delete on-disk cover files
        // (base + resized variants) for cover TYPES the current manga no longer has. Guards
        // against a reused SQLite rowid inheriting a previous tenant's cover folder when the
        // MangaDeletedEvent folder-delete loses the race with this handler or fails. Best
        // effort — a failure here must never abort the cover sync, so it is fully swallowed.
        private void PruneOrphanedCovers(Manga.Manga manga)
        {
            try
            {
                var folder = Path.Combine(_coverRootFolder, manga.Id.ToString());
                if (!_diskProvider.FolderExists(folder))
                {
                    return;
                }

                // Filename prefixes the current manga legitimately owns, e.g. "poster" for a
                // Poster cover — matches both the base "poster.jpg" and resized "poster-250.jpg".
                var ownedPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var cover in manga.Images ?? new List<MediaCover>())
                {
                    if (cover.CoverType != MediaCoverTypes.Unknown)
                    {
                        ownedPrefixes.Add(cover.CoverType.ToString().ToLowerInvariant());
                    }
                }

                foreach (var file in _diskProvider.GetFiles(folder, recursive: false))
                {
                    var name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();

                    // "poster-250" -> "poster"; "poster" -> "poster".
                    var dashIndex = name.IndexOf('-');
                    var prefix = dashIndex >= 0 ? name.Substring(0, dashIndex) : name;

                    if (!ownedPrefixes.Contains(prefix))
                    {
                        try
                        {
                            _diskProvider.DeleteFile(file);
                            _logger.Debug("Pruned orphaned cover file {0} for manga {1}", file, manga.Id);
                        }
                        catch (Exception ex)
                        {
                            _logger.Debug(ex, "Couldn't prune orphaned cover file {0} for manga {1}", file, manga.Id);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to prune orphaned cover files for manga {0}", manga.Id);
            }
        }

        private void DownloadCover(string localPath, string remoteUrl)
        {
            _logger.Info("Downloading manga cover {0}", remoteUrl);
            _httpClient.DownloadFile(remoteUrl, localPath);
        }

        // <paramref name="regenerate"/> — when true, an existing resized variant is deleted
        // before re-resizing instead of being skipped. The MangaUpdatedEvent path passes true
        // because it only reaches EnsureResized after DownloadCover rewrote the base file:
        // skipping a resized variant that already exists would serve a stale cover on a reused
        // manga Id (see debug session wrong-cover-image-after-add).
        private void EnsureResized(string localPath, int mangaId, MediaCoverTypes coverType, bool regenerate = false)
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
                var resizedExists = _diskProvider.FileExists(resizedPath);

                if (resizedExists && regenerate)
                {
                    try
                    {
                        _diskProvider.DeleteFile(resizedPath);
                        resizedExists = false;
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Couldn't delete stale resized manga cover {0}-{1} for manga {2}", coverType, h, mangaId);
                    }
                }

                if (!resizedExists)
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
