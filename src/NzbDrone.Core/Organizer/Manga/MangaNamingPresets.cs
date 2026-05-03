using System.Collections.Generic;

namespace NzbDrone.Core.Organizer.Manga
{
    // Sonarr divergence: NEW per Phase 5 D-16 — see DIVERGENCE.md.
    // Reader-compat preset templates web-verified per 05-RESEARCH.md Pattern 5 (Komga: Komga
    // docs + GitHub discussion #1295; Kavita: parser tests `c003`, `Ch. 0001`, `025.5`;
    // ComicRack: legacy `#NNN` issue marker convention).
    // Default = Komga per D-16 (most widely deployed *arr-stack-pairing reader).
    // Phase 8 cleanup: collapse Organizer/Manga into canonical Organizer/ namespace when Tv/ deletes.
    public sealed record MangaNamingPreset(
        string Name,
        string StandardChapterFormat,
        string MangaFolderFormat,
        string Description);

    public static class MangaNamingPresets
    {
        // Locked per 05-RESEARCH.md Pattern 5 — DO NOT change template strings without revisiting Phase 5 discuss-phase signoff.
        public static readonly IReadOnlyList<MangaNamingPreset> All = new[]
        {
            new MangaNamingPreset(
                Name: "Komga",
                StandardChapterFormat: "{Manga.Title} - Chapter {Chapter.Number:000}",
                MangaFolderFormat: "{Manga.Title}",
                Description: "Komga default — flat manga folders; series-folder + chapter-file naming. Reader scans 1 level deep into manga folders. 3-digit padding handles up to 999 chapters cleanly."),

            new MangaNamingPreset(
                Name: "Kavita",
                StandardChapterFormat: "{Manga.Title} Ch.{Chapter.Number:0000}",
                MangaFolderFormat: "{Manga.Title}",
                Description: "Kavita default — uses 'Ch.NNNN' marker (4-digit padding); flat manga folders. Kavita parser explicitly tested against this shape."),

            new MangaNamingPreset(
                Name: "ComicRack",
                StandardChapterFormat: "{Manga.Title} #{Chapter.Number:000}",
                MangaFolderFormat: "{Manga.Title}",
                Description: "ComicRack legacy convention — '#NNN' issue marker; flat manga folders. 3-digit padding aligns with Komga template for consistency."),

            new MangaNamingPreset(
                Name: "Custom",
                StandardChapterFormat: "{Manga.Title} - Chapter {Chapter.Number:000}",
                MangaFolderFormat: "{Manga.Title}",
                Description: "Starting point for custom templates. Edit fields freely.")
        };

        public static MangaNamingPreset Default => All[0];   // Komga per Phase 5 D-16
    }
}
