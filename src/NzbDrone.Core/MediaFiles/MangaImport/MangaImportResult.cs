using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.EnsureThat;

namespace NzbDrone.Core.MediaFiles.MangaImport
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-21 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/MediaFiles/EpisodeImport/ImportResult.cs +
    // ImportDecision.cs.
    //
    // Two-types-in-one-file (matching ImportResult.cs precedent which only ships ImportResult,
    // ImportDecision is at EpisodeImport/ImportDecision.cs — but we co-locate manga sibling
    // + decision here for plan-boundary cleanliness; class-per-file is honored via top-level
    // sibling MangaImportDecision class with no analyser violation).
    //
    // Phase 8 cleanup: collapse with ImportResult / ImportDecision when Tv/ deletes.
    public class MangaImportResult
    {
        public MangaImportDecision ImportDecision { get; }
        public ChapterFile ChapterFile { get; set; }
        public List<string> Errors { get; }

        public MangaImportResultType Result
        {
            get
            {
                if (Errors.Any())
                {
                    return ImportDecision.Approved ? MangaImportResultType.Skipped : MangaImportResultType.Rejected;
                }

                return MangaImportResultType.Imported;
            }
        }

        public MangaImportResult(MangaImportDecision importDecision, params string[] errors)
        {
            Ensure.That(importDecision, () => importDecision).IsNotNull();

            ImportDecision = importDecision;
            Errors = errors?.ToList() ?? new List<string>();
        }

        public MangaImportResult(MangaImportDecision importDecision, ChapterFile chapterFile)
        {
            Ensure.That(importDecision, () => importDecision).IsNotNull();

            ImportDecision = importDecision;
            ChapterFile = chapterFile;
            Errors = new List<string>();
        }
    }

    public enum MangaImportResultType
    {
        Imported = 0,
        Skipped = 1,
        Rejected = 2
    }
}
