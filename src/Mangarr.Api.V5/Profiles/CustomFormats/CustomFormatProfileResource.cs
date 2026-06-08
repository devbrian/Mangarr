using Mangarr.Http.REST;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.CustomFormats;

namespace Mangarr.Api.V5.Profiles.CustomFormats;

// Sonarr divergence: NEW V5 resource per Phase 5 D-07 — see DIVERGENCE.md.
public class CustomFormatProfileResource : RestResource
{
    public string? Name { get; set; }
    public int MinFormatScore { get; set; }
    public int? MaxFormatScore { get; set; }
    public List<CustomFormatProfileFormatItemResource> FormatItems { get; set; } = [];

    // GH-followup (quick-260608-gmm): the entity carries UpgradeAllowed (Phase 6 D-10, default
    // false) and UpgradeSpecification.cs reads it as the INNER half of the D-10 three-state
    // effective-upgrade-allowed AND-merge. Phase 5 D-07 shipped this resource WITHOUT the field,
    // freezing it at the C# default false and rendering the editor's Upgrades-Allowed checkbox
    // indeterminate (undefined on the wire). Exposed here to round-trip it (mirrors the sibling
    // TranslationProfileResource.UpgradeAllowed fix for GH #138).
    public bool UpgradeAllowed { get; set; }

    // IsDefault is NOT a column on CustomFormatProfile — the default profile is the global
    // Config.DefaultCustomFormatProfileId. The controller computes this on read (== this Id) and,
    // on create/update with IsDefault==true, points the config key at this profile. The mapper
    // leaves it false; only the controller (which has IConfigService) sets it.
    public bool IsDefault { get; set; }
}

public class CustomFormatProfileFormatItemResource : RestResource
{
    public int Format { get; set; }
    public string? Name { get; set; }
    public int Score { get; set; }
}

public static class CustomFormatProfileResourceMapper
{
    public static CustomFormatProfileResource ToResource(this CustomFormatProfile model)
    {
        return new CustomFormatProfileResource
        {
            Id = model.Id,
            Name = model.Name,
            MinFormatScore = model.MinFormatScore,
            MaxFormatScore = model.MaxFormatScore,
            UpgradeAllowed = model.UpgradeAllowed,
            FormatItems = model.FormatItems.ConvertAll(ToResource)
        };
    }

    public static CustomFormatProfileFormatItemResource ToResource(this ProfileFormatItem model)
    {
        return new CustomFormatProfileFormatItemResource
        {
            Format = model.Format.Id,
            Name = model.Format.Name,
            Score = model.Score
        };
    }

    public static CustomFormatProfile ToModel(this CustomFormatProfileResource resource)
    {
        return new CustomFormatProfile
        {
            Id = resource.Id,
            Name = resource.Name,
            MinFormatScore = resource.MinFormatScore,
            MaxFormatScore = resource.MaxFormatScore,
            UpgradeAllowed = resource.UpgradeAllowed,
            FormatItems = resource.FormatItems.ConvertAll(ToModel)
        };
    }

    public static ProfileFormatItem ToModel(this CustomFormatProfileFormatItemResource resource)
    {
        return new ProfileFormatItem
        {
            Format = new CustomFormat { Id = resource.Format },
            Score = resource.Score
        };
    }

    public static List<CustomFormatProfileResource> ToResource(this IEnumerable<CustomFormatProfile> models)
    {
        return models.Select(ToResource).ToList();
    }
}
