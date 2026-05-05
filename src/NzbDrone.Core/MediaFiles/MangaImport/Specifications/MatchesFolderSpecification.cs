using System.IO;
using NLog;
using NzbDrone.Core.Download;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Parser.Manga;

namespace NzbDrone.Core.MediaFiles.MangaImport.Specifications
{
    // Sonarr divergence: NEW manga sibling per Phase 8 PARITY-02 backfill — see
    // `.planning/phases/08-tv-manga-parity-audit/audit/no-sibling/MatchesFolderSpecification.md`.
    // Role-match analog: src/NzbDrone.Core/MediaFiles/EpisodeImport/Specifications/
    // MatchesFolderSpecification.cs.
    //
    // Catches the "wrong file in folder" failure mode for manual-import / auto-scan
    // flows where a chapter file is dropped into the wrong manga's library folder
    // (e.g., `Berserk-Chapter-301.cbz` placed inside the `One Piece` directory).
    //
    // TV's spec performs file-vs-folder ParsedEpisodeInfo set-difference on
    // EpisodeNumbers because TV folders may be season folders containing multiple
    // episodes. Manga has no equivalent multi-axis ambiguity: a manga library
    // folder maps 1:1 to a single Manga aggregate (Phase 5 D-15 path builder), so
    // the manga check collapses to a Manga-identity comparison — resolve the
    // parent folder name to a Manga and reject when it disagrees with the
    // LocalChapter's resolved Manga.
    //
    // ExistingFile short-circuits to Accept (mirrors TV) so manual re-imports of
    // pre-existing files in their canonical home don't trip the check.
    //
    // Phase 8 cleanup: collapse with TV MatchesFolderSpecification when Tv/ deletes.
    public class MatchesFolderSpecification : IMangaImportDecisionEngineSpecification
    {
        private readonly IMangaService _mangaService;
        private readonly Logger _logger;

        public MatchesFolderSpecification(IMangaService mangaService, Logger logger)
        {
            _mangaService = mangaService;
            _logger = logger;
        }

        public MangaImportSpecDecision IsSatisfiedBy(LocalChapter localChapter, DownloadClientItem downloadClientItem)
        {
            if (localChapter.ExistingFile)
            {
                return MangaImportSpecDecision.Accept();
            }

            if (string.IsNullOrWhiteSpace(localChapter.Path))
            {
                _logger.Debug("LocalChapter has no Path, skipping folder check");
                return MangaImportSpecDecision.Accept();
            }

            var folderPath = Path.GetDirectoryName(localChapter.Path);
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                _logger.Debug("Could not determine parent folder for {0}, skipping check", localChapter.Path);
                return MangaImportSpecDecision.Accept();
            }

            var folderName = Path.GetFileName(folderPath);
            if (string.IsNullOrWhiteSpace(folderName))
            {
                _logger.Debug("Empty folder name for {0}, skipping check", localChapter.Path);
                return MangaImportSpecDecision.Accept();
            }

            // Single source of truth normalization (Parser/Manga D-05) so the
            // folder lookup matches what AddMangaService dedup + MangaParsingService
            // use — ensures consistent identity across resolver paths.
            var clean = MangaTitleNormalizer.Normalize(folderName);
            if (string.IsNullOrWhiteSpace(clean))
            {
                _logger.Debug("Folder name '{0}' normalized to empty, skipping check", folderName);
                return MangaImportSpecDecision.Accept();
            }

            var folderManga = _mangaService.FindByTitle(clean);

            // No manga matched the folder name — the file may live in a non-canonical
            // root (e.g., a generic "Manga" parent or a misnamed folder). Mirror the
            // TV behavior of skipping rather than rejecting when the folder side of
            // the comparison is unresolvable.
            if (folderManga == null)
            {
                _logger.Debug(
                    "No Manga matched folder name '{0}' (normalized '{1}'), skipping check",
                    folderName,
                    clean);
                return MangaImportSpecDecision.Accept();
            }

            if (localChapter.Manga == null)
            {
                _logger.Debug("LocalChapter.Manga is null, skipping check");
                return MangaImportSpecDecision.Accept();
            }

            if (folderManga.Id != localChapter.Manga.Id)
            {
                _logger.Debug(
                    "File parsed as Manga '{0}' (id={1}) but parent folder '{2}' resolved to Manga '{3}' (id={4})",
                    localChapter.Manga.Title,
                    localChapter.Manga.Id,
                    folderName,
                    folderManga.Title,
                    folderManga.Id);

                return MangaImportSpecDecision.Reject(
                    ImportRejectionReason.ChapterUnexpected,
                    "Chapter for {0} was unexpected considering the {1} folder name",
                    localChapter.Manga.Title,
                    folderName);
            }

            return MangaImportSpecDecision.Accept();
        }
    }
}
