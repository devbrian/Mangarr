using System.Collections.Generic;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.CustomFormats
{
    // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption —
    // TV-shape fields (EpisodeInfo / Series / ParsedEpisodeInfo) stripped per
    // Plan 15-03 Tv/ DELETE + Plan 15-10 Parser/Model TV DELETE.
    // Manga-side custom-format input lives in MangaCustomFormatInput.cs (peer).
    // The shared fields below (Size / IndexerFlags / Languages / Filename / ReleaseType)
    // remain in this base because manga + non-manga (future) inputs share them.
    public class CustomFormatInput
    {
        public long Size { get; set; }
        public IndexerFlags IndexerFlags { get; set; }
        public List<Language> Languages { get; set; }
        public string Filename { get; set; }
        public ReleaseType ReleaseType { get; set; }

        public CustomFormatInput()
        {
            Languages = new List<Language>();
        }
    }
}
