using System;
using System.Collections.Generic;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Notifications.Komga
{
    // Sonarr divergence: NEW manga-reader provider per Phase 6 D-15 + D-17 + D-18 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Notifications/MediaBrowser/MediaBrowser.cs.
    // D-18: only OnChapterImport is overridden; all other Supports* default false via base reflection.
    // Pitfall 2: LibraryId is REQUIRED (validator enforces; Komga has no scan-all endpoint).
    // Pattern 7: 5-second debounce per LibraryId via MediaServerUpdateQueue<KomgaNotification, int>
    // info-only overload — 50 chapter imports collapse to ONE scan call per library.
    // Phase 8 cleanup: this class's TV-shaped sibling notifications (OnGrab/OnDownload) stay no-op.
    public class KomgaNotification : NotificationBase<KomgaNotificationSettings>
    {
        private readonly IKomgaService _service;
        private readonly IKomgaProxy _proxy;
        private readonly MediaServerUpdateQueue<KomgaNotification, int> _updateQueue;
        private readonly Logger _logger;

        public KomgaNotification(IKomgaService service, IKomgaProxy proxy, ICacheManager cacheManager, Logger logger)
        {
            _service = service;
            _proxy = proxy;
            _updateQueue = new MediaServerUpdateQueue<KomgaNotification, int>(cacheManager);
            _logger = logger;
        }

        public override string Link => "https://komga.org/";
        public override string Name => "Komga";

        // Phase 6 D-18 + Pitfall 7: ONLY override OnChapterImport. All other On* virtuals
        // stay default no-op; their Supports* flags resolve to false via reflection.
        public override void OnChapterImport(ChapterImportMessage message)
        {
            // Defensive — validator (Pitfall 2) should have blocked save without LibraryId.
            // We silently no-op rather than throw to keep the notification fan-out resilient.
            if (!Settings.LibraryId.HasValue)
            {
                return;
            }

            _updateQueue.Add(QueueIdentifier(Settings), Settings.LibraryId.Value);
        }

        public override void ProcessQueue()
        {
            // Action<int> overload — info-only debounce drain, one call per unique LibraryId.
            Action<int> drain = libraryId =>
            {
                try
                {
                    _proxy.Scan(Settings);
                    _logger.Debug("Komga rescan flushed for library {0}", libraryId);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Komga rescan failed for library {0}", libraryId);
                }
            };

            _updateQueue.ProcessQueue(QueueIdentifier(Settings), drain);
        }

        public override ValidationResult Test()
        {
            var failures = new List<ValidationFailure>();
            failures.AddIfNotNull(_service.Test(Settings));
            return new ValidationResult(failures);
        }

        // Per-Settings.Url cache key so two Komga providers pointed at different servers do not
        // share their pending-scan queue.
        private static string QueueIdentifier(KomgaNotificationSettings settings)
        {
            return settings.Url ?? string.Empty;
        }
    }
}
