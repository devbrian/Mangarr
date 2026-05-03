using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Download;
using NzbDrone.Core.Manga;

namespace NzbDrone.Core.MediaFiles.MangaImport.Specifications
{
    // Sonarr divergence: NEW manga sibling per Phase 6 PIPELINE-04 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/MediaFiles/EpisodeImport/Specifications/
    // MatchesGrabSpecification.cs.
    //
    // Reject if the chapter actually parsed from the staging CBZ does not match the
    // chapters the original Phase 6 search/grab decision approved. This catches the
    // "downloaded the wrong thing" failure mode (e.g., MangaDex feed shifted between
    // grab + completion). Mirrors TV's set-difference check on EpisodeIds → ChapterIds
    // (Plan 06-07 D-21 — Phase 6 added ReleaseInfo.ChapterIds for this purpose).
    //
    // Plan body says ExistingFile short-circuits; we honor the same shortcut so manual
    // re-imports of pre-existing files (no Release context) bypass the check.
    //
    // Phase 8 cleanup: collapse with TV MatchesGrabSpecification when Tv/ deletes.
    public class MatchesGrabSpecification : IMangaImportDecisionEngineSpecification
    {
        private readonly Logger _logger;

        public MatchesGrabSpecification(Logger logger)
        {
            _logger = logger;
        }

        public MangaImportSpecDecision IsSatisfiedBy(LocalChapter localChapter, DownloadClientItem downloadClientItem)
        {
            if (localChapter.ExistingFile)
            {
                return MangaImportSpecDecision.Accept();
            }

            var release = localChapter.Release;
            if (release == null)
            {
                return MangaImportSpecDecision.Accept();
            }

            var releaseChapterIds = release.ChapterIds ?? new List<int>();
            if (releaseChapterIds.Count == 0)
            {
                // Pre-Phase-6 release rows or release rows produced before search-time
                // ChapterId stamping (e.g., manual import) don't carry the metadata; the
                // safe behavior matches TV (accept rather than block legit imports).
                return MangaImportSpecDecision.Accept();
            }

            var unexpected = (localChapter.Chapters ?? new List<Chapter>())
                .Where(c => !releaseChapterIds.Contains(c.Id))
                .ToList();

            if (unexpected.Any())
            {
                _logger.Debug(
                    "Unexpected chapter(s) {0} in file {1} — release {2} grabbed for chapters {3}",
                    string.Join(",", unexpected.Select(c => c.Id)),
                    localChapter.Path,
                    release.Title,
                    string.Join(",", releaseChapterIds));

                if (unexpected.Count == 1)
                {
                    return MangaImportSpecDecision.Reject(
                        ImportRejectionReason.ChapterNotFoundInRelease,
                        "Chapter {0} was not found in the grabbed release: {1}",
                        unexpected[0].Id,
                        release.Title);
                }

                return MangaImportSpecDecision.Reject(
                    ImportRejectionReason.ChapterNotFoundInRelease,
                    "Chapters {0} were not found in the grabbed release: {1}",
                    string.Join(",", unexpected.Select(c => c.Id)),
                    release.Title);
            }

            return MangaImportSpecDecision.Accept();
        }
    }
}
