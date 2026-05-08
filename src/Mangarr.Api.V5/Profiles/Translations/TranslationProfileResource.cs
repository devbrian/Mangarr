using NzbDrone.Core.Profiles.Translations;
using Mangarr.Http.REST;

namespace Mangarr.Api.V5.Profiles.Translations;

// Sonarr divergence: NEW V5 resource per Phase 5 D-01 — see DIVERGENCE.md.
public class TranslationProfileResource : RestResource
{
    public string? Name { get; set; }
    public List<string> Languages { get; set; } = [];
    public bool AllowLanguagesNotInProfile { get; set; }
}

public static class TranslationProfileResourceMapper
{
    public static TranslationProfileResource ToResource(this TranslationProfile model) => new()
    {
        Id = model.Id,
        Name = model.Name,
        Languages = model.Languages,
        AllowLanguagesNotInProfile = model.AllowLanguagesNotInProfile
    };

    public static TranslationProfile ToModel(this TranslationProfileResource resource) => new()
    {
        Id = resource.Id,
        Name = resource.Name,
        Languages = resource.Languages,
        AllowLanguagesNotInProfile = resource.AllowLanguagesNotInProfile
    };

    public static List<TranslationProfileResource> ToResource(this IEnumerable<TranslationProfile> models)
        => models.Select(ToResource).ToList();
}
