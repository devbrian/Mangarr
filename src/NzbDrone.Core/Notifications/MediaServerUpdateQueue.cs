using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Cache;

namespace NzbDrone.Core.Notifications
{
    // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption —
    // TV-shape Series-keyed overloads + UpdateQueueItem<TItemInfo>(Series series) class
    // stripped per Plan 15-03 Tv/ DELETE. Manga-side info-only debounce queue is the only
    // surviving surface (used by Komga + Kavita via Pattern 7 — 50 chapter imports
    // collapse to one scan call per library; coalesces by TItemInfo).
    public class MediaServerUpdateQueue<TQueueHost, TItemInfo>
        where TQueueHost : class
    {
        private class InfoOnlyQueue
        {
            public HashSet<TItemInfo> Pending { get; } = new HashSet<TItemInfo>();
            public bool Refreshing { get; set; }
        }

        private readonly ICached<InfoOnlyQueue> _pendingInfoCache;

        public MediaServerUpdateQueue(ICacheManager cacheManager)
        {
            _pendingInfoCache = cacheManager.GetRollingCache<InfoOnlyQueue>(typeof(TQueueHost), "pendingInfo", TimeSpan.FromDays(1));
        }

        // Mangarr Phase 6 — info-only overload (Pattern 7 — e.g., 50 chapter imports for one
        // Komga LibraryId collapse to one scan).
        public void Add(string identifier, TItemInfo info)
        {
            var queue = _pendingInfoCache.Get(identifier, () => new InfoOnlyQueue());

            lock (queue)
            {
                queue.Pending.Add(info);
            }
        }

        // Mangarr Phase 6 — info-only ProcessQueue overload paired with Add(string, TItemInfo).
        // Drains the deduplicated TItemInfo set; invoked once per unique key.
        public void ProcessQueue(string identifier, Action<TItemInfo> update)
        {
            var queue = _pendingInfoCache.Find(identifier);

            if (queue == null)
            {
                return;
            }

            lock (queue)
            {
                if (queue.Refreshing)
                {
                    return;
                }

                queue.Refreshing = true;
            }

            try
            {
                while (true)
                {
                    List<TItemInfo> items;

                    lock (queue)
                    {
                        if (queue.Pending.Count == 0)
                        {
                            queue.Refreshing = false;
                            return;
                        }

                        items = queue.Pending.ToList();
                        queue.Pending.Clear();
                    }

                    foreach (var item in items)
                    {
                        update(item);
                    }
                }
            }
            catch
            {
                lock (queue)
                {
                    queue.Refreshing = false;
                }

                throw;
            }
        }
    }
}
