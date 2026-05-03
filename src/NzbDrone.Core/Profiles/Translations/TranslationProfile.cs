using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Profiles.Translations
{
    // Sonarr divergence: NEW profile entity per Phase 5 D-01 (cf-only-walkthrough.md verdict
    // signed off 2026-05-01: "TranslationProfile added") — see DIVERGENCE.md.
    // Sibling to QualityProfile; persisted as a separate "TranslationProfiles" table.
    // Languages : List<string> persists as JSON column via the StringListConverter<List<string>>
    // already registered at TableMapping.cs:233 (PATTERNS-MAP Adaptation Hotspot 8 — no new converter).
    // AllowLanguagesNotInProfile defaults to FALSE per Phase 5 D-02 (strict mode — vast majority of
    // users have one language and only want that language; user direction 2026-05-03).
    // Phase 8 cleanup: collapse Profiles/Translations into canonical Profiles/ namespace when Tv/ deletes.
    public class TranslationProfile : ModelBase
    {
        public TranslationProfile()
        {
            Languages = new List<string>();
        }

        public string Name { get; set; }
        public List<string> Languages { get; set; }            // ordered BCP-47 codes; index = preference rank (D-03)
        public bool AllowLanguagesNotInProfile { get; set; }   // D-02; default false (strict mode)
    }
}
