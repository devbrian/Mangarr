using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Notifications
{
    public class MediaServerUpdateQueue<TQueueHost, TItemInfo>
        where TQueueHost : class
    {
        private class UpdateQueue
        {
            public Dictionary<int, UpdateQueueItem<TItemInfo>> Pending { get; } = new Dictionary<int, UpdateQueueItem<TItemInfo>>();
            public bool Refreshing { get; set; }
        }

        // Mangarr Phase 6 D-15 / Pattern 7 — info-only debounce queue used by manga reader
        // notifications (Komga, Kavita) that have no Series object to coalesce on. Coalesce
        // happens by TItemInfo (e.g., LibraryId : int) per cache identifier.
        private class InfoOnlyQueue
        {
            public HashSet<TItemInfo> Pending { get; } = new HashSet<TItemInfo>();
            public bool Refreshing { get; set; }
        }

        private readonly ICached<UpdateQueue> _pendingSeriesCache;
        private readonly ICached<InfoOnlyQueue> _pendingInfoCache;

        public MediaServerUpdateQueue(ICacheManager cacheManager)
        {
            _pendingSeriesCache = cacheManager.GetRollingCache<UpdateQueue>(typeof(TQueueHost), "pendingSeries", TimeSpan.FromDays(1));
            _pendingInfoCache = cacheManager.GetRollingCache<InfoOnlyQueue>(typeof(TQueueHost), "pendingInfo", TimeSpan.FromDays(1));
        }

        public void Add(string identifier, Series series, TItemInfo info)
        {
            var queue = _pendingSeriesCache.Get(identifier, () => new UpdateQueue());

            lock (queue)
            {
                var item = queue.Pending.TryGetValue(series.Id, out var value)
                    ? value
                    : new UpdateQueueItem<TItemInfo>(series);

                item.Info.Add(info);

                queue.Pending[series.Id] = item;
            }
        }

        // Mangarr Phase 6 — info-only overload. No Series object required; coalesces by TItemInfo
        // (Pattern 7 — e.g., 50 chapter imports for one Komga LibraryId collapse to one scan).
        public void Add(string identifier, TItemInfo info)
        {
            var queue = _pendingInfoCache.Get(identifier, () => new InfoOnlyQueue());

            lock (queue)
            {
                queue.Pending.Add(info);
            }
        }

        public void ProcessQueue(string identifier, Action<List<UpdateQueueItem<TItemInfo>>> update)
        {
            var queue = _pendingSeriesCache.Find(identifier);

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
                    List<UpdateQueueItem<TItemInfo>> items;

                    lock (queue)
                    {
                        if (queue.Pending.Empty())
                        {
                            queue.Refreshing = false;
                            return;
                        }

                        items = queue.Pending.Values.ToList();
                        queue.Pending.Clear();
                    }

                    update(items);
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

    public class UpdateQueueItem<TItemInfo>
    {
        public Series Series { get; set; }
        public HashSet<TItemInfo> Info { get; set; }

        public UpdateQueueItem(Series series)
        {
            Series = series;
            Info = new HashSet<TItemInfo>();
        }
    }
}
