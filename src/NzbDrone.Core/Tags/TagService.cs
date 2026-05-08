using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Download;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Profiles.Delay;
using NzbDrone.Core.Profiles.Releases;

namespace NzbDrone.Core.Tags
{
    // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption —
    // ISeriesService / IImportListFactory / IAutoTaggingService deps stripped
    // (TV-only or no-longer-bound). IMangaService used as manga peer for
    // tag-aggregate lookups (AllForTag).
    public interface ITagService
    {
        Tag GetTag(int tagId);
        Tag GetTag(string tag);
        List<Tag> GetTags(IEnumerable<int> ids);
        TagDetails Details(int tagId);
        List<TagDetails> Details();
        List<Tag> All();
        Tag Add(Tag tag);
        Tag Update(Tag tag);
        void Delete(int tagId);
    }

    public class TagService : ITagService
    {
        private readonly ITagRepository _repo;
        private readonly IEventAggregator _eventAggregator;
        private readonly IDelayProfileService _delayProfileService;
        private readonly INotificationFactory _notificationFactory;
        private readonly IReleaseProfileService _releaseProfileService;
        private readonly IMangaService _mangaService;
        private readonly IIndexerFactory _indexerService;
        private readonly IDownloadClientFactory _downloadClientFactory;

        public TagService(ITagRepository repo,
                          IEventAggregator eventAggregator,
                          IDelayProfileService delayProfileService,
                          INotificationFactory notificationFactory,
                          IReleaseProfileService releaseProfileService,
                          IMangaService mangaService,
                          IIndexerFactory indexerService,
                          IDownloadClientFactory downloadClientFactory)
        {
            _repo = repo;
            _eventAggregator = eventAggregator;
            _delayProfileService = delayProfileService;
            _notificationFactory = notificationFactory;
            _releaseProfileService = releaseProfileService;
            _mangaService = mangaService;
            _indexerService = indexerService;
            _downloadClientFactory = downloadClientFactory;
        }

        public Tag GetTag(int tagId)
        {
            return _repo.Get(tagId);
        }

        public Tag GetTag(string tag)
        {
            if (tag.All(char.IsDigit))
            {
                return _repo.Get(int.Parse(tag));
            }
            else
            {
                return _repo.GetByLabel(tag);
            }
        }

        public List<Tag> GetTags(IEnumerable<int> ids)
        {
            return _repo.Get(ids).ToList();
        }

        public TagDetails Details(int tagId)
        {
            var tag = GetTag(tagId);
            var delayProfiles = _delayProfileService.AllForTag(tagId);
            var notifications = _notificationFactory.AllForTag(tagId);
            var releaseProfiles = _releaseProfileService.AllForTag(tagId);
            var excludedReleaseProfiles = _releaseProfileService.AllExcludedForTag(tagId);
            var manga = _mangaService.AllForTag(tagId);
            var indexers = _indexerService.AllForTag(tagId);
            var downloadClients = _downloadClientFactory.AllForTag(tagId);

            return new TagDetails
            {
                Id = tagId,
                Label = tag.Label,
                DelayProfileIds = delayProfiles.Select(c => c.Id).ToList(),
                NotificationIds = notifications.Select(c => c.Id).ToList(),
                RestrictionIds = releaseProfiles.Select(c => c.Id).ToList(),
                ExcludedReleaseProfileIds = excludedReleaseProfiles.Select(c => c.Id).ToList(),
                MangaIds = manga.Select(c => c.Id).ToList(),
                IndexerIds = indexers.Select(c => c.Id).ToList(),
                DownloadClientIds = downloadClients.Select(c => c.Id).ToList()
            };
        }

        public List<TagDetails> Details()
        {
            var tags = All();
            var delayProfiles = _delayProfileService.All();
            var notifications = _notificationFactory.All();
            var releaseProfiles = _releaseProfileService.All();
            var excludedReleaseProfiles = _releaseProfileService.All();
            var manga = _mangaService.AllForTag(0); // empty filter — All() not currently exposed for manga tags
            var indexers = _indexerService.All();
            var downloadClients = _downloadClientFactory.All();

            var details = new List<TagDetails>();

            foreach (var tag in tags)
            {
                details.Add(new TagDetails
                {
                    Id = tag.Id,
                    Label = tag.Label,
                    DelayProfileIds = delayProfiles.Where(c => c.Tags.Contains(tag.Id)).Select(c => c.Id).ToList(),
                    NotificationIds = notifications.Where(c => c.Tags.Contains(tag.Id)).Select(c => c.Id).ToList(),
                    RestrictionIds = releaseProfiles.Where(c => c.Tags.Contains(tag.Id)).Select(c => c.Id).ToList(),
                    ExcludedReleaseProfileIds = excludedReleaseProfiles.Where(c => c.ExcludedTags.Contains(tag.Id)).Select(c => c.Id).ToList(),
                    MangaIds = _mangaService.AllForTag(tag.Id).Select(c => c.Id).ToList(),
                    IndexerIds = indexers.Where(c => c.Tags.Contains(tag.Id)).Select(c => c.Id).ToList(),
                    DownloadClientIds = downloadClients.Where(c => c.Tags.Contains(tag.Id)).Select(c => c.Id).ToList(),
                });
            }

            return details;
        }

        public List<Tag> All()
        {
            return _repo.All().OrderBy(t => t.Label).ToList();
        }

        public Tag Add(Tag tag)
        {
            var existingTag = _repo.FindByLabel(tag.Label);

            if (existingTag != null)
            {
                return existingTag;
            }

            tag.Label = tag.Label.ToLowerInvariant();

            _repo.Insert(tag);
            _eventAggregator.PublishEvent(new TagsUpdatedEvent());

            return tag;
        }

        public Tag Update(Tag tag)
        {
            tag.Label = tag.Label.ToLowerInvariant();

            _repo.Update(tag);
            _eventAggregator.PublishEvent(new TagsUpdatedEvent());

            return tag;
        }

        public void Delete(int tagId)
        {
            var details = Details(tagId);
            if (details.InUse)
            {
                throw new ModelConflictException(typeof(Tag), tagId, $"'{details.Label}' cannot be deleted since it's still in use");
            }

            _repo.Delete(tagId);
            _eventAggregator.PublishEvent(new TagsUpdatedEvent());
        }
    }
}
