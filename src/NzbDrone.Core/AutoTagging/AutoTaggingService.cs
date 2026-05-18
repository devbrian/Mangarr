using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.AutoTagging
{
    public interface IAutoTaggingService
    {
        void Update(AutoTag autoTag);
        AutoTag Insert(AutoTag autoTag);
        List<AutoTag> All();
        AutoTag GetById(int id);
        void Delete(int id);
        List<AutoTag> AllForTag(int tagId);
        AutoTaggingChanges GetTagChanges(NzbDrone.Core.Manga.Manga manga);
    }

    public class AutoTaggingService : IAutoTaggingService
    {
        private readonly IAutoTaggingRepository _repository;
        private readonly IRootFolderService _rootFolderService;
        private readonly IEventAggregator _eventAggregator;
        private readonly ICached<Dictionary<int, AutoTag>> _cache;

        public AutoTaggingService(IAutoTaggingRepository repository,
                                  IRootFolderService rootFolderService,
                                  IEventAggregator eventAggregator,
                                  ICacheManager cacheManager)
        {
            _repository = repository;
            _rootFolderService = rootFolderService;
            _eventAggregator = eventAggregator;

            _cache = cacheManager.GetCache<Dictionary<int, AutoTag>>(typeof(AutoTag), "autoTags");
        }

        private Dictionary<int, AutoTag> AllDictionary()
        {
            return _cache.Get("all", () => _repository.All().ToDictionary(m => m.Id));
        }

        public List<AutoTag> All()
        {
            return AllDictionary().Values.ToList();
        }

        public AutoTag GetById(int id)
        {
            return AllDictionary()[id];
        }

        public void Update(AutoTag autoTag)
        {
            _repository.Update(autoTag);

            _cache.Clear();
            _eventAggregator.PublishEvent(new AutoTagsUpdatedEvent());
        }

        public AutoTag Insert(AutoTag autoTag)
        {
            var result = _repository.Insert(autoTag);

            _cache.Clear();
            _eventAggregator.PublishEvent(new AutoTagsUpdatedEvent());

            return result;
        }

        public void Delete(int id)
        {
            _repository.Delete(id);

            _cache.Clear();
            _eventAggregator.PublishEvent(new AutoTagsUpdatedEvent());
        }

        public List<AutoTag> AllForTag(int tagId)
        {
            return All().Where(p => p.Tags.Contains(tagId))
                .ToList();
        }

        public AutoTaggingChanges GetTagChanges(NzbDrone.Core.Manga.Manga manga)
        {
            var autoTags = All();
            var changes = new AutoTaggingChanges();

            if (autoTags.Empty())
            {
                return changes;
            }

            // Set the root folder path on the manga
            manga.RootFolderPath = _rootFolderService.GetBestRootFolderPath(manga.Path);

            foreach (var autoTag in autoTags)
            {
                var specificationMatches = autoTag.Specifications
                    .GroupBy(t => t.GetType())
                    .Select(g => new SpecificationMatchesGroup
                    {
                        Matches = g.ToDictionary(t => t, t => t.IsSatisfiedBy(manga))
                    })
                    .ToList();

                var allMatch = specificationMatches.All(x => x.DidMatch);
                var tags = autoTag.Tags;

                if (allMatch)
                {
                    foreach (var tag in tags)
                    {
                        if (!manga.Tags.Contains(tag))
                        {
                            changes.TagsToAdd.Add(tag);
                        }
                    }

                    continue;
                }

                if (autoTag.RemoveTagsAutomatically)
                {
                    // Mangarr divergence from Sonarr `6f857ba0e^`: when rule A
                    // matches and would add tag T, and rule B does NOT match and
                    // has RemoveTagsAutomatically=true with tag T in its tag set,
                    // Sonarr's verbatim algorithm puts T in BOTH TagsToAdd and
                    // TagsToRemove. The applier's apply-order (Add foreach then
                    // Remove foreach) means Remove silently wins, even though
                    // another rule matched. Exclude tags already in TagsToAdd so
                    // additions decisively win over conflicting removals.
                    foreach (var tag in tags)
                    {
                        if (!changes.TagsToAdd.Contains(tag))
                        {
                            changes.TagsToRemove.Add(tag);
                        }
                    }
                }
            }

            return changes;
        }
    }
}
