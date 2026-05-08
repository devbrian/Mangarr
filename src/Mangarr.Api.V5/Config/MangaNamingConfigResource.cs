using NzbDrone.Core.Organizer;
using Sonarr.Http.REST;

namespace Mangarr.Api.V5.Config;

// Sonarr divergence: NEW V5 resource per Phase 5 D-13 + Open Question 4 — see DIVERGENCE.md.
// V5 sibling to V3 NamingConfigController; carries ONLY the manga-shaped fields.
// (Existing TV NamingSettingsController stays untouched until Phase 8 collapse.)
public class MangaNamingConfigResource : RestResource
{
    public bool RenameChapters { get; set; }
    public bool ReplaceIllegalCharacters { get; set; }                 // shared with TV
    public int ColonReplacementFormat { get; set; }                    // shared with TV; persisted as enum
    public string? CustomColonReplacementFormat { get; set; }          // shared with TV
    public string? StandardChapterFormat { get; set; }
    public string? MangaFolderFormat { get; set; }
}

public static class MangaNamingConfigResourceMapper
{
    public static MangaNamingConfigResource ToMangaResource(this NamingConfig model)
    {
        return new MangaNamingConfigResource
        {
            Id = model.Id,
            RenameChapters = model.RenameChapters,
            ReplaceIllegalCharacters = model.ReplaceIllegalCharacters,
            ColonReplacementFormat = (int)model.ColonReplacementFormat,
            CustomColonReplacementFormat = model.CustomColonReplacementFormat,
            StandardChapterFormat = model.StandardChapterFormat,
            MangaFolderFormat = model.MangaFolderFormat
        };
    }

    // Apply manga-shaped fields onto an existing NamingConfig. Preserves all TV-shaped fields
    // (StandardEpisodeFormat / SeasonFolderFormat / etc.) so the singleton stays intact.
    // Pitfall 9 mitigation: NamingConfigService.Save invalidates ConfigService caches via the
    // standard Upsert path — do not write the row directly.
    public static void ApplyMangaFields(this NamingConfig target, MangaNamingConfigResource resource)
    {
        target.RenameChapters = resource.RenameChapters;
        target.ReplaceIllegalCharacters = resource.ReplaceIllegalCharacters;
        target.ColonReplacementFormat = (ColonReplacementFormat)resource.ColonReplacementFormat;
        target.CustomColonReplacementFormat = resource.CustomColonReplacementFormat ?? string.Empty;
        target.StandardChapterFormat = resource.StandardChapterFormat;
        target.MangaFolderFormat = resource.MangaFolderFormat;
    }
}
