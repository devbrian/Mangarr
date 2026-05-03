using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.CustomFormats.Events;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Profiles.CustomFormats
{
    public interface ICustomFormatProfileService
    {
        CustomFormatProfile Add(CustomFormatProfile profile);
        void Update(CustomFormatProfile profile);
        void Delete(int id);
        List<CustomFormatProfile> All();
        CustomFormatProfile Get(int id);
        bool Exists(int id);
    }

    // Sonarr divergence: NEW service per Phase 5 D-07 + D-11 — see DIVERGENCE.md.
    // Mirrors QualityProfileService shape with three KEY divergences:
    //   1. MaxFormatScore is NULLABLE on the entity (Adaptation Hotspot 4)
    //   2. NO Languages list (lives on TranslationProfile per D-07 orthogonal split)
    //   3. NO UpgradeAllowed/Cutoff (TV-quality-specific)
    //
    // First-run seeder per pattern S4 + Open Question 2 recommendation: idempotent
    // `if (All().Any()) return;` then Add("Default" / MinFormatScore=0 / MaxFormatScore=null /
    // FormatItems pre-populated with every existing CF at score=0). Pitfall 8 mitigation:
    // Delete raises InUseException if profile is assigned to any Manga OR is the global default.
    // CF event handlers mirror QualityProfileService.cs:159-191 verbatim — auto-insert/remove
    // ProfileFormatItem when CFs come/go.
    //
    // Note (executor deviation): IMangaService exposes GetAllManga() (not AllManga() as the
    // plan body suggested). This is the canonical method per Phase 2 IMangaService contract.
    //
    // Phase 8 cleanup: collapse to canonical Profiles/ namespace when Tv/ deletes.
    public class CustomFormatProfileService : ICustomFormatProfileService,
                                              IHandle<ApplicationStartedEvent>,
                                              IHandle<CustomFormatAddedEvent>,
                                              IHandle<CustomFormatDeletedEvent>
    {
        private readonly ICustomFormatProfileRepository _repository;
        private readonly IConfigService _configService;
        private readonly IMangaService _mangaService;
        private readonly ICustomFormatService _formatService;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public CustomFormatProfileService(ICustomFormatProfileRepository repository,
                                          IConfigService configService,
                                          IMangaService mangaService,
                                          ICustomFormatService formatService,
                                          IEventAggregator eventAggregator,
                                          Logger logger)
        {
            _repository = repository;
            _configService = configService;
            _mangaService = mangaService;
            _formatService = formatService;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public CustomFormatProfile Add(CustomFormatProfile profile)
        {
            return _repository.Insert(profile);
        }

        public void Update(CustomFormatProfile profile)
        {
            _repository.Update(profile);
            _eventAggregator.PublishEvent(new CustomFormatProfileUpdatedEvent(profile.Id));
        }

        public void Delete(int id)
        {
            // Pitfall 8 — block delete if assigned to any Manga OR if it's the global default.
            if (_mangaService.GetAllManga().Any(m => m.CustomFormatProfileId == id) ||
                _configService.DefaultCustomFormatProfileId == id)
            {
                var profile = _repository.Get(id);
                throw new CustomFormatProfileInUseException(profile.Name);
            }

            _repository.Delete(id);
        }

        public List<CustomFormatProfile> All()
        {
            return _repository.All().ToList();
        }

        public CustomFormatProfile Get(int id)
        {
            return _repository.Get(id);
        }

        public bool Exists(int id)
        {
            return _repository.Exists(id);
        }

        public void Handle(ApplicationStartedEvent message)
        {
            // Pattern S4 — idempotent on restart.
            if (All().Any())
            {
                return;
            }

            _logger.Info("Setting up default custom format profile (no score gate)");

            // Per Open Question 2 + RESEARCH Example 2: pre-populate FormatItems with every
            // existing CF at score=0 so Update calls don't blank the list.
            var profile = new CustomFormatProfile
            {
                Name = "Default",
                MinFormatScore = 0,
                MaxFormatScore = null,         // no cap (D-07)
                FormatItems = _formatService.All()
                                            .Select(f => new ProfileFormatItem { Score = 0, Format = f })
                                            .ToList()
            };

            Add(profile);

            // Pitfall 8 — seed the global default so first-add Manga FK isn't orphaned.
            // BasicRepository.Insert mutates the entity in-place to set Id (Dapper round-trip),
            // so reading profile.Id here is safe in production. In unit tests with a mocked
            // repository, Insert mock setups should also assign Id (see fixture setup).
            _configService.DefaultCustomFormatProfileId = profile.Id;
        }

        // Mirror QualityProfileService.cs:159-173 verbatim — auto-insert new CF at score=0 on every profile.
        public void Handle(CustomFormatAddedEvent message)
        {
            var all = All();

            foreach (var profile in all)
            {
                profile.FormatItems.Insert(0, new ProfileFormatItem
                {
                    Score = 0,
                    Format = message.CustomFormat
                });

                Update(profile);
            }
        }

        // Mirror QualityProfileService.cs:175-191 verbatim — auto-remove deleted CF from every profile.
        public void Handle(CustomFormatDeletedEvent message)
        {
            var all = All();

            foreach (var profile in all)
            {
                profile.FormatItems = profile.FormatItems.Where(c => c.Format.Id != message.CustomFormat.Id).ToList();

                if (profile.FormatItems.Empty())
                {
                    profile.MinFormatScore = 0;
                }

                Update(profile);
            }
        }
    }
}
