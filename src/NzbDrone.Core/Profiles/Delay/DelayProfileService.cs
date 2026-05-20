using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Profiles.Delay
{
    public interface IDelayProfileService
    {
        DelayProfile Add(DelayProfile profile);
        DelayProfile Update(DelayProfile profile);
        void Delete(int id);
        List<DelayProfile> All();
        DelayProfile Get(int id);
        List<DelayProfile> AllForTag(int tagId);
        List<DelayProfile> AllForTags(HashSet<int> tagIds);
        DelayProfile BestForTags(HashSet<int> tagIds);
        List<DelayProfile> Reorder(int id, int? afterId);
    }

    // Sonarr-canonical first-run seeder added per issue #62 (convergence, not divergence).
    // Mirrors codebase-canonical Pattern S4 (TranslationProfileService precedent at
    // src/NzbDrone.Core/Profiles/Translations/TranslationProfileService.cs:92-113): runtime
    // IHandle<ApplicationStartedEvent> seeder rather than migration Insert.IntoTable row, per
    // 001_mangarr_baseline.cs:580-587 "Migrations create schema only" guidance.
    //
    // Why a seeder at all: MangaPendingReleaseService.GetDelay uses `.First()` on
    // AllForTags(remoteChapter.Manga.Tags). When a manga has zero tags AND no default
    // (tag-less) DelayProfile row exists, .First() throws InvalidOperationException —
    // Sonarr never hit this because 001_initial_setup seeds the default row. This codebase's
    // migration policy prohibits Insert.IntoTable seeds (see Phase 2 retro at
    // 001_mangarr_baseline.cs:580-587), so the canonical fix is the runtime seeder pattern.
    public class DelayProfileService : IDelayProfileService, IHandle<ApplicationStartedEvent>
    {
        private readonly IDelayProfileRepository _repo;
        private readonly ICached<DelayProfile> _bestForTagsCache;
        private readonly Logger _logger;

        public DelayProfileService(IDelayProfileRepository repo, ICacheManager cacheManager, Logger logger)
        {
            _repo = repo;
            _bestForTagsCache = cacheManager.GetCache<DelayProfile>(GetType(), "best");
            _logger = logger;
        }

        public DelayProfile Add(DelayProfile profile)
        {
            profile.Order = _repo.Count();

            var result = _repo.Insert(profile);
            _bestForTagsCache.Clear();

            return result;
        }

        public DelayProfile Update(DelayProfile profile)
        {
            var result = _repo.Update(profile);
            _bestForTagsCache.Clear();
            return result;
        }

        public void Delete(int id)
        {
            _repo.Delete(id);

            var all = All().OrderBy(d => d.Order).ToList();

            for (var i = 0; i < all.Count; i++)
            {
                if (all[i].Id == 1)
                {
                    continue;
                }

                all[i].Order = i + 1;
            }

            _repo.UpdateMany(all);
            _bestForTagsCache.Clear();
        }

        public List<DelayProfile> All()
        {
            return _repo.All().ToList();
        }

        public DelayProfile Get(int id)
        {
            return _repo.Get(id);
        }

        public List<DelayProfile> AllForTag(int tagId)
        {
            return All().Where(r => r.Tags.Contains(tagId))
                        .ToList();
        }

        public List<DelayProfile> AllForTags(HashSet<int> tagIds)
        {
            return All().Where(r => r.Tags.Intersect(tagIds).Any() || r.Tags.Empty()).ToList();
        }

        public DelayProfile BestForTags(HashSet<int> tagIds)
        {
            var key = "-" + tagIds.Select(v => v.ToString()).Join(",");
            return _bestForTagsCache.Get(key, () => FetchBestForTags(tagIds), TimeSpan.FromSeconds(30));
        }

        private DelayProfile FetchBestForTags(HashSet<int> tagIds)
        {
            return _repo.All()
                        .Where(r => r.Tags.Intersect(tagIds).Any() || r.Tags.Empty())
                        .OrderBy(d => d.Order).First();
        }

        public List<DelayProfile> Reorder(int id, int? afterId)
        {
            var all = All().OrderBy(d => d.Order)
                           .ToList();

            var moving = all.SingleOrDefault(d => d.Id == id);
            var after = afterId.HasValue ? all.SingleOrDefault(d => d.Id == afterId) : null;

            if (moving == null)
            {
                // TODO: This should throw
                return all;
            }

            var afterOrder = GetAfterOrder(moving, after);
            var afterCount = afterOrder + 2;
            var movingOrder = moving.Order;

            foreach (var delayProfile in all)
            {
                if (delayProfile.Id == 1)
                {
                    continue;
                }

                if (delayProfile.Id == id)
                {
                    delayProfile.Order = afterOrder + 1;
                }
                else if (delayProfile.Id == after?.Id)
                {
                    delayProfile.Order = afterOrder;
                }
                else if (delayProfile.Order > afterOrder)
                {
                    delayProfile.Order = afterCount;
                    afterCount++;
                }
                else if (delayProfile.Order > movingOrder)
                {
                    delayProfile.Order--;
                }
            }

            _repo.UpdateMany(all);

            return All();
        }

        private int GetAfterOrder(DelayProfile moving, DelayProfile after)
        {
            if (after == null)
            {
                return 0;
            }

            if (moving.Order < after.Order)
            {
                return after.Order - 1;
            }

            return after.Order;
        }

        public void Handle(ApplicationStartedEvent message)
        {
            // Pattern S4 — idempotent on restart. Mirror TranslationProfileService.Handle.
            // Issue #62: ensures AllForTags(emptyTags) never returns an empty list, so
            // MangaPendingReleaseService.GetDelay's .First() call is always safe.
            if (All().Any())
            {
                return;
            }

            _logger.Info("Setting up default delay profile (tag-less, Http delay 0)");

            // Sonarr-canonical default: tag-less profile, Order=int.MaxValue (sorted last so
            // any user-added tagged profile takes precedence). PreferredProtocol=Http is the
            // only active protocol in this codebase (Phase 15 D-18 — Indexers/DownloadProtocol.cs
            // strips Usenet=1 + Torrent=2 leaving only Http=3). Order=int.MaxValue matches the
            // Sonarr DelayProfileServiceFixture.Setup sentinel for the default row.
            //
            // NOTE: insert via _repo.Insert directly — Add() rewrites profile.Order to _repo.Count()
            // which would clobber the int.MaxValue sentinel to 0 on first run.
            // Phase 26 Plan 26-03 (DP-02) — payload trimmed atomic with Migration 003.
            // The 4 dead protocol-delay fields were dropped from entity + DDL in same
            // commit per Pitfall 1 (partial commit breaks runtime seeder).
            _repo.Insert(new DelayProfile
            {
                PreferredProtocol = DownloadProtocol.Http,
                HttpDelay = 0,
                Order = int.MaxValue,
                Tags = new HashSet<int>()
            });

            _bestForTagsCache.Clear();
        }
    }
}
