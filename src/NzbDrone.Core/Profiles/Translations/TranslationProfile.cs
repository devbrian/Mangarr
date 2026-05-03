using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Profiles.Translations
{
    // Sonarr divergence: NEW profile entity per Phase 5 D-01 (cf-only-walkthrough.md verdict) — see DIVERGENCE.md.
    // Wave 0 stub — Wave 1 (plan 05-02) replaces the body with the full Name + Languages + AllowLanguagesNotInProfile shape.
    // Phase 8 cleanup: collapse Profiles/Translations into canonical Profiles/ namespace when Tv/ deletes.
    public class TranslationProfile : ModelBase
    {
    }
}
