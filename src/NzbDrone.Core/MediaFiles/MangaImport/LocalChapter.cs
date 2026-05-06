using System.Collections.Generic;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.MangaImport
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-21 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/Parser/Model/LocalEpisode.cs.
    //
    // POCO carrying the staging CBZ path + resolved Manga + Chapter aggregate that
    // MangaImportDecisionMaker / ImportApprovedChapters operate on. Mirrors
    // LocalEpisode's "single import candidate" shape, with manga divergences:
    //   * Episodes (List<Episode>) → Chapter (single) + Chapters (List<Chapter>)
    //     — the multi-element list matches MatchesGrabSpecification iteration shape;
    //     single-chapter releases populate both with the same single entry.
    //   * Quality (QualityModel) → CustomFormatScore (int) + TranslatedLanguage —
    //     manga has no quality model (Phase 5 D-04). Comparison ordering lives in
    //     MangaDownloadDecisionComparer (TranslationProfile rank → CF score).
    //   * ParsedEpisodeInfo → ParsedChapterInfo (Phase 2 D-09 manga parser output).
    //   * Release : ReleaseInfo — direct ReleaseInfo (Phase 3 manga indexer pipeline
    //     contract) rather than the TV-only GrabbedReleaseInfo wrapper. Plan 06-07
    //     ImportApprovedChapters hydrates ScanlationGroup / TranslatedLanguage from
    //     this for ChapterFile provenance.
    //   * ExistingFile = true short-circuits idempotency / upgrade specs (mirrors TV).
    //
    // Phase 8 cleanup: collapse with LocalEpisode when Tv/ deletes.
    public class LocalChapter
    {
        public LocalChapter()
        {
            Chapters = new List<Chapter>();
            CustomFormats = new List<CustomFormat>();
        }

        public string Path { get; set; }
        public long Size { get; set; }
        public NzbDrone.Core.Manga.Manga Manga { get; set; }
        public Chapter Chapter { get; set; }
        public List<Chapter> Chapters { get; set; }
        public ReleaseInfo Release { get; set; }
        public ParsedChapterInfo ParsedChapterInfo { get; set; }
        public DownloadClientItemInfo DownloadItem { get; set; }
        public bool ExistingFile { get; set; }
        public string TranslatedLanguage { get; set; }
        public string ScanlationGroup { get; set; }
        public List<CustomFormat> CustomFormats { get; set; }
        public int CustomFormatScore { get; set; }
        public bool ScriptImported { get; set; }

        public override string ToString()
        {
            return Path;
        }
    }

    // Lightweight type to avoid leaking the full Download.DownloadClientItem here. The
    // canonical DI surface uses the real DownloadClientItem at the spec / orchestrator
    // boundary; this nested struct keeps LocalChapter free of circular usings.
    public class DownloadClientItemInfo
    {
        public string DownloadId { get; set; }
        public string Title { get; set; }
    }
}
