using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Profiles.Translations
{
    public interface ITranslationProfileService
    {
        TranslationProfile Add(TranslationProfile profile);
        void Update(TranslationProfile profile);
        void Delete(int id);
        List<TranslationProfile> All();
        TranslationProfile Get(int id);
        bool Exists(int id);
    }

    // Sonarr divergence: NEW service per Phase 5 D-01 + D-11 — see DIVERGENCE.md.
    // Mirrors QualityProfileService shape but DROPS the per-CF FormatItems handling
    // (TranslationProfile carries no CF score items — that lives on CustomFormatProfile per D-07).
    // First-run seeder per pattern S4: idempotent `if (All().Any()) return;` guard then
    // Add("English Only" / Languages=["en"] / AllowLanguagesNotInProfile=false) per D-11
    // first-run-UX dependency. Pitfall 8 mitigation: Delete raises InUseException if profile
    // is assigned to any Manga OR is the global default. Phase 8 cleanup: collapse to
    // canonical Profiles/ namespace when Tv/ deletes.
    public class TranslationProfileService : ITranslationProfileService,
                                              IHandle<ApplicationStartedEvent>
    {
        private readonly ITranslationProfileRepository _repository;
        private readonly IConfigService _configService;
        private readonly IMangaService _mangaService;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public TranslationProfileService(ITranslationProfileRepository repository,
                                         IConfigService configService,
                                         IMangaService mangaService,
                                         IEventAggregator eventAggregator,
                                         Logger logger)
        {
            _repository = repository;
            _configService = configService;
            _mangaService = mangaService;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public TranslationProfile Add(TranslationProfile profile)
        {
            return _repository.Insert(profile);
        }

        public void Update(TranslationProfile profile)
        {
            _repository.Update(profile);
            _eventAggregator.PublishEvent(new TranslationProfileUpdatedEvent(profile.Id));
        }

        public void Delete(int id)
        {
            // Pitfall 8 — block delete if assigned to any Manga OR if it's the global default.
            // Mirror QualityProfileService.Delete (line 65-74) verbatim with type swap; note
            // IMangaService surface uses GetAllManga() (not AllManga() — see Manga/IMangaService.cs).
            if (_mangaService.GetAllManga().Any(m => m.TranslationProfileId == id) ||
                _configService.DefaultTranslationProfileId == id)
            {
                var profile = _repository.Get(id);
                throw new TranslationProfileInUseException(profile.Name);
            }

            _repository.Delete(id);
        }

        public List<TranslationProfile> All()
        {
            return _repository.All().ToList();
        }

        public TranslationProfile Get(int id)
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

            _logger.Info("Setting up default translation profile (English Only, strict mode)");

            var profile = Add(new TranslationProfile
            {
                Name = "English Only",
                Languages = new List<string> { "en" },
                AllowLanguagesNotInProfile = false   // D-02 strict mode
            });

            // Pitfall 8 — seed the global default Config key so first-add Manga FK isn't orphaned.
            // Per D-11 first-run-UX dependency: empty default CF bundle means this seeded
            // TranslationProfile is the ONLY filter on first-run.
            _configService.DefaultTranslationProfileId = profile.Id;
        }
    }
}
