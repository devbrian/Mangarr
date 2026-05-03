using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Profiles.CustomFormats
{
    // Sonarr divergence: NEW profile entity per Phase 5 D-07 — see DIVERGENCE.md.
    // CF score thresholds live on a SEPARATE entity from TranslationProfile per D-07
    // (orthogonal concerns: language preference and CF scoring). Sibling to QualityProfile
    // and to TranslationProfile; persisted as a separate "CustomFormatProfiles" table.
    //
    // KEY DIVERGENCES from QualityProfile (per Phase 5 PATTERNS-MAP Adaptation Hotspots 4 + 9):
    //  - MaxFormatScore : int? is NULLABLE (Sonarr QualityProfile has no max-score concept; null = no cap per D-07)
    //  - FormatItems shipped per Open Question 2 researcher recommendation (power users want
    //    per-profile per-CF score override; persists via the EmbeddedDocumentConverter<List<ProfileFormatItem>>
    //    already registered at TableMapping.cs:213)
    //  - DROPS QualityProfile's UpgradeAllowed/Cutoff/Items/MinUpgradeFormatScore/CutoffFormatScore (TV-quality-specific)
    //  - DROPS Languages list (lives on the sibling TranslationProfile per D-07 orthogonal split)
    //
    // Phase 8 cleanup: collapse Profiles/CustomFormats into canonical Profiles/ namespace when Tv/ deletes.
    public class CustomFormatProfile : ModelBase
    {
        public CustomFormatProfile()
        {
            FormatItems = new List<ProfileFormatItem>();
        }

        public string Name { get; set; }
        public int MinFormatScore { get; set; }                // D-07 default 0
        public int? MaxFormatScore { get; set; }               // D-07 default null = no cap (NULLABLE divergence per Adaptation Hotspot 4)
        public List<ProfileFormatItem> FormatItems { get; set; }   // per-profile per-CF score override (Open Question 2)
    }
}
