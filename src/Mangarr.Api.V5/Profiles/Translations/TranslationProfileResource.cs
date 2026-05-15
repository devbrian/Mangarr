using Mangarr.Http.REST;
using NzbDrone.Core.Profiles.Translations;

namespace Mangarr.Api.V5.Profiles.Translations;

// Sonarr divergence: NEW V5 resource per Phase 5 D-01 — see DIVERGENCE.md.
public class TranslationProfileResource : RestResource
{
    public string? Name { get; set; }
    public List<string> Languages { get; set; } = [];
    public bool AllowLanguagesNotInProfile { get; set; }

    // GH #138: the entity carries UpgradeAllowed (Phase 6 D-10, default true) and
    // UpgradeSpecification.cs reads it as the OUTER half of the D-10 three-state
    // effective-upgrade-allowed AND-merge. Phase 5 D-01 shipped this resource without
    // the field, freezing it at the C# default true and making it unconfigurable from
    // the UI. Exposed here to round-trip it (mirrors Sonarr QualityProfile.UpgradeAllowed).
    public bool UpgradeAllowed { get; set; } = true;
}

public static class TranslationProfileResourceMapper
{
    public static TranslationProfileResource ToResource(this TranslationProfile model) => new()
    {
        Id = model.Id,
        Name = model.Name,
        Languages = model.Languages,
        AllowLanguagesNotInProfile = model.AllowLanguagesNotInProfile,
        UpgradeAllowed = model.UpgradeAllowed
    };

    public static TranslationProfile ToModel(this TranslationProfileResource resource) => new()
    {
        Id = resource.Id,
        Name = resource.Name,
        Languages = resource.Languages,
        AllowLanguagesNotInProfile = resource.AllowLanguagesNotInProfile,
        UpgradeAllowed = resource.UpgradeAllowed
    };

    public static List<TranslationProfileResource> ToResource(this IEnumerable<TranslationProfile> models)
        => models.Select(ToResource).ToList();
}
