using System;
using System.Collections.Generic;

namespace NzbDrone.Core.MediaFiles
{
    // Sonarr divergence: NEW manga sibling per Phase 9 D-09-03 #1 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/MediaFiles/MediaFileExtensions.cs.
    //
    // Manga archive extensions per RESEARCH §State of the Art line 423 — extracted
    // from the manga ManualImportService allowlist for shared use by MangaDiskScanService.
    //
    // Phase 14 cleanup: collapse with MediaFileExtensions when Tv/ deletes (likely keep
    // as separate constant — manga and video formats remain distinct concepts).
    public static class MangaFileExtensions
    {
        public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".cbz",
            ".cbr",
            ".zip",
            ".cb7"
        };
    }
}
