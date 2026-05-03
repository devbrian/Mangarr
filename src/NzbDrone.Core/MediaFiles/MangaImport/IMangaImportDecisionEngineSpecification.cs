using NzbDrone.Core.Download;

namespace NzbDrone.Core.MediaFiles.MangaImport
{
    // Sonarr divergence: NEW manga interface per Phase 6 D-21 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/MediaFiles/EpisodeImport/IImportDecisionEngineSpecification.cs.
    //
    // Compile-error-driven contract (Phase 3 LEARNINGS): ship the interface FIRST so
    // MangaImportDecisionMaker.GetDecision compile-errors drive each spec class.
    //
    // Auto-discovered via DryIoc IEnumerable<IMangaImportDecisionEngineSpecification>
    // ctor injection — matches Phase 5 manga-spec auto-discovery (Phase 4 LEARNINGS S1).
    //
    // Phase 8 cleanup: collapse with TV IImportDecisionEngineSpecification when Tv/ deletes.
    public interface IMangaImportDecisionEngineSpecification
    {
        MangaImportSpecDecision IsSatisfiedBy(LocalChapter localChapter, DownloadClientItem downloadClientItem);
    }
}
