using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Profiles.CustomFormats
{
    // Sonarr divergence: NEW profile entity per Phase 5 D-07 — see DIVERGENCE.md.
    // Wave 0 stub — Wave 1 (plan 05-03) replaces the body with the full Name + MinFormatScore + MaxFormatScore + FormatItems shape.
    // Phase 8 cleanup: collapse Profiles/CustomFormats into canonical Profiles/ namespace when Tv/ deletes.
    public class CustomFormatProfile : ModelBase
    {
    }
}
