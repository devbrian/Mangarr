using System;
using System.Collections.Generic;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Notifications.Kavita
{
    // Sonarr divergence: NEW manga-reader provider per Phase 6 D-16 + D-17 + D-18 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Notifications/Komga/KomgaNotification.cs (sibling provider
    // shipped in Plan 06-10) and src/NzbDrone.Core/Notifications/MediaBrowser/MediaBrowser.cs.
    // D-18: only OnChapterImport is overridden; all other Supports* default false via base reflection.
    // D-16 (vs Komga's Pitfall 2): Kavita HAS a scan-all endpoint, so LibraryId is OPTIONAL.
    // Pattern 7: 5-second debounce per LibraryId via MediaServerUpdateQueue<KavitaNotification, int>
    // info-only overload — 50 chapter imports collapse to ONE scan call per (Url, LibraryId).
    // For scan-all (LibraryId == null), we use 0 as the dedup sentinel inside this provider's queue;
    // 0 cannot collide with any real LibraryId because the validator rejects LibraryId <= 0 when set.
    // Phase 8 cleanup: stays as-is (manga-only sibling under Notifications/Kavita/).
    public class KavitaNotification : NotificationBase<KavitaNotificationSettings>
    {
        private readonly IKavitaProxy _proxy;
        private readonly IKavitaService _service;
        private readonly MediaServerUpdateQueue<KavitaNotification, int> _updateQueue;
        private readonly Logger _logger;

        public KavitaNotification(IKavitaProxy proxy, IKavitaService service, ICacheManager cacheManager, Logger logger)
        {
            _proxy = proxy;
            _service = service;
            _updateQueue = new MediaServerUpdateQueue<KavitaNotification, int>(cacheManager);
            _logger = logger;
        }

        public override string Link => "https://kavitareader.com/";
        public override string Name => "Kavita";

        // Phase 6 D-18 + Pitfall 7: ONLY override OnChapterImport. All other On* virtuals
        // stay default no-op; their Supports* flags resolve to false via reflection.
        public override void OnChapterImport(ChapterImportMessage message)
        {
            // LibraryId.Value or 0 sentinel for scan-all dedup within this provider instance.
            _updateQueue.Add(QueueIdentifier(Settings), Settings.LibraryId ?? 0);
        }

        public override void ProcessQueue()
        {
            // Action<int> overload — info-only debounce drain. The queue collapses repeated
            // adds with the same LibraryId int into a single drain entry, so this lambda fires
            // ONCE per unique (Url, LibraryId) cluster regardless of how many chapters arrived.
            Action<int> drain = libraryId =>
            {
                try
                {
                    _proxy.Scan(Settings);
                    _logger.Debug("Kavita rescan flushed for library {0}", libraryId);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Kavita rescan failed for library {0}", libraryId);
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

        // Per-Settings.Url cache identifier so two Kavita providers configured against different
        // servers do not share their pending-scan queue.
        private static string QueueIdentifier(KavitaNotificationSettings settings)
        {
            return settings.Url ?? string.Empty;
        }
    }
}
