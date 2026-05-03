using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.CustomFormats;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Profiles.CustomFormats;

// Sonarr divergence: NEW V5 resource per Phase 5 D-07 — see DIVERGENCE.md.
public class CustomFormatProfileResource : RestResource
{
    public string? Name { get; set; }
    public int MinFormatScore { get; set; }
    public int? MaxFormatScore { get; set; }
    public List<CustomFormatProfileFormatItemResource> FormatItems { get; set; } = [];
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
