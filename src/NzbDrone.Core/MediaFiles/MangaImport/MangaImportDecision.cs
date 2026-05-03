using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.MediaFiles.MangaImport
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-21 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/MediaFiles/EpisodeImport/ImportDecision.cs.
    //
    // Per-LocalChapter aggregate of MangaImportSpecDecision rejections from each
    // IMangaImportDecisionEngineSpecification. Approved == zero rejections.
    // Phase 8 cleanup: collapse with ImportDecision when Tv/ deletes.
    public class MangaImportDecision
    {
        public LocalChapter LocalChapter { get; }
        public List<MangaImportRejection> Rejections { get; }
        public bool Approved => !Rejections.Any();

        public MangaImportDecision(LocalChapter localChapter, params MangaImportRejection[] rejections)
        {
            LocalChapter = localChapter;
            Rejections = (rejections ?? System.Array.Empty<MangaImportRejection>())
                .Where(r => r != null)
                .ToList();
        }
    }

    public class MangaImportRejection
    {
        public ImportRejectionReason Reason { get; }
        public string Message { get; }

        public MangaImportRejection(ImportRejectionReason reason, string message)
        {
            Reason = reason;
            Message = message;
        }

        public override string ToString() => Message;
    }
}
