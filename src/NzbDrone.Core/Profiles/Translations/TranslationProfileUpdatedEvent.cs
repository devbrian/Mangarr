using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Profiles.Translations;

// Sonarr divergence: NEW event per Phase 5 D-01 — see DIVERGENCE.md.
// Published by TranslationProfileService.Update so downstream caches/listeners can invalidate.
// Mirrors Sonarr's QualityProfileUpdatedEvent shape (primary-constructor / file-scoped namespace).
public class TranslationProfileUpdatedEvent(int profileId) : IEvent
{
    public int ProfileId { get; private set; } = profileId;
}
