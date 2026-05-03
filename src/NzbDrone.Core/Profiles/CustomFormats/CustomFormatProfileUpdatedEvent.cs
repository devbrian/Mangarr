using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Profiles.CustomFormats;

// Sonarr divergence: NEW event per Phase 5 D-07 — see DIVERGENCE.md.
// Mirrors QualityProfileUpdatedEvent (sibling primary-ctor pattern at Profiles/Qualities/QualityProfileUpdatedEvent.cs).
public class CustomFormatProfileUpdatedEvent(int id) : IEvent
{
    public int Id { get; private set; } = id;
}
