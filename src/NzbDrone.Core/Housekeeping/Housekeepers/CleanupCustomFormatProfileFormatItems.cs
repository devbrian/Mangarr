using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.CustomFormats;

namespace NzbDrone.Core.Housekeeping.Housekeepers
{
    // Phase 8 backfill 13-04: manga sibling of TV `CleanupQualityProfileFormatItems` (audit no-sibling/single).
    //
    // Note on plan-symbol naming: Plan 08-13-04 was filed as `CleanupTranslationProfileFormatItems`,
    // but `TranslationProfile` (Phase 5 D-01) carries no `FormatItems` — only `Languages`. The audit
    // report body explicitly targets `CleanupCustomFormatProfileFormatItems` against
    // `CustomFormatProfile` (Phase 5 D-07), which is the entity that owns FormatItems on the manga
    // side per the orthogonal-concerns split. This housekeeper therefore mirrors TV behavior against
    // CustomFormatProfile — the only profile shape with a real gap.
    //
    // Defensive periodic safety net: even though `CustomFormatProfileService` already handles
    // `CustomFormatAddedEvent` / `CustomFormatDeletedEvent` to keep FormatItems in sync, this
    // housekeeper guards against missed events / DB drift / direct DB edits / restored backups.
    //
    // Divergences from TV impl (per Phase 5 D-07 / PATTERNS-MAP Adaptation Hotspots 4 + 9):
    //  - Empty-FormatItems reset clamps `MinFormatScore = 0` and `MaxFormatScore = null`. TV's
    //    `CutoffFormatScore` / `MinUpgradeFormatScore` columns DO NOT exist on CustomFormatProfile
    //    (dropped per D-07), so they are not reset.
    //  - SetFields targets `FormatItems`, `MinFormatScore`, `MaxFormatScore` only.
    public class CleanupCustomFormatProfileFormatItems : IHousekeepingTask
    {
        private readonly ICustomFormatProfileFormatItemsCleanupRepository _repository;
        private readonly ICustomFormatRepository _customFormatRepository;

        public CleanupCustomFormatProfileFormatItems(ICustomFormatProfileFormatItemsCleanupRepository repository,
                                                     ICustomFormatRepository customFormatRepository)
        {
            _repository = repository;
            _customFormatRepository = customFormatRepository;
        }

        public void Clean()
        {
            var customFormats = _customFormatRepository.All().ToDictionary(c => c.Id);
            var profiles = _repository.All();
            var updatedProfiles = new List<CustomFormatProfile>();

            foreach (var profile in profiles)
            {
                var formatItems = new List<ProfileFormatItem>();

                // Make sure the profile doesn't include formats that have been removed
                profile.FormatItems.ForEach(p =>
                {
                    if (p.Format != null && customFormats.ContainsKey(p.Format.Id))
                    {
                        formatItems.Add(p);
                    }
                });

                // Make sure the profile includes all available formats
                foreach (var customFormat in customFormats)
                {
                    if (formatItems.None(f => f.Format.Id == customFormat.Key))
                    {
                        formatItems.Insert(0, new ProfileFormatItem
                        {
                            Format = customFormat.Value,
                            Score = 0
                        });
                    }
                }

                var previousIds = profile.FormatItems.Select(i => i.Format.Id).ToList();
                var ids = formatItems.Select(i => i.Format.Id).ToList();

                // Update the profile if any formats were added or removed
                if (ids.Except(previousIds).Any() || previousIds.Except(ids).Any())
                {
                    profile.FormatItems = formatItems;

                    if (profile.FormatItems.Empty())
                    {
                        // CustomFormatProfile divergence: only MinFormatScore + MaxFormatScore exist
                        // (CutoffFormatScore / MinUpgradeFormatScore dropped per Phase 5 D-07).
                        profile.MinFormatScore = 0;
                        profile.MaxFormatScore = null;
                    }

                    updatedProfiles.Add(profile);
                }
            }

            if (updatedProfiles.Any())
            {
                _repository.SetFields(updatedProfiles, p => p.FormatItems, p => p.MinFormatScore, p => p.MaxFormatScore);
            }
        }
    }

    public interface ICustomFormatProfileFormatItemsCleanupRepository : IBasicRepository<CustomFormatProfile>
    {
    }

    public class CustomFormatProfileFormatItemsCleanupRepository : BasicRepository<CustomFormatProfile>, ICustomFormatProfileFormatItemsCleanupRepository
    {
        public CustomFormatProfileFormatItemsCleanupRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }
}
