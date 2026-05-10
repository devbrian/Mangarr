using System.Collections.Generic;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.MangaImport.Manual
{
    // Sonarr divergence: NEW manga sibling per Phase 8 parity audit (no-sibling/ManualImportItem).
    // Role-match analog: src/NzbDrone.Core/MediaFiles/EpisodeImport/Manual/ManualImportItem.cs.
    //
    // Per-row response POCO for the GET /api/v5/manualimport?folder=... endpoint backing the
    // InteractiveImport modal. Mirrors TV ManualImportItem with manga divergences:
    //   * Series → Manga (Phase 5 domain rename).
    //   * SeasonNumber dropped (manga has no seasons per LocalChapter).
    //   * Episodes (List<Episode>) → Chapters (List<Chapter>).
    //   * EpisodeFileId → ChapterFileId.
    //   * QualityModel + List<Language> + ReleaseGroup absorbed into ScanlationGroup
    //     (Phase 16.1 D-04 + D-06 — scanlation groups ARE the release groups for manga;
    //     ReleaseGroup originally dropped Phase 5 D-04, mirrors LocalChapter shape).
    //   * Rejections IEnumerable<ImportRejection> → IEnumerable<MangaImportRejection>.
    //
    // Phase 8 cleanup: collapse with TV ManualImportItem when Tv/ deletes.
    public class ManualImportItem
    {
        public string Path { get; set; }
        public string RelativePath { get; set; }
        public string FolderName { get; set; }
        public string Name { get; set; }
        public long Size { get; set; }
        public NzbDrone.Core.Manga.Manga Manga { get; set; }
        public List<NzbDrone.Core.Manga.Chapter> Chapters { get; set; }
        public int? ChapterFileId { get; set; }
        public string TranslatedLanguage { get; set; }
        public string ScanlationGroup { get; set; }
        public string DownloadId { get; set; }
        public List<CustomFormat> CustomFormats { get; set; } = new();
        public int CustomFormatScore { get; set; }
        public int IndexerFlags { get; set; }
        public ReleaseType ReleaseType { get; set; }
        public IEnumerable<MangaImportRejection> Rejections { get; set; } = new List<MangaImportRejection>();
    }
}
