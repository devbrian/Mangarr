using NzbDrone.Core.Parser.Manga.Model;

namespace NzbDrone.Core.DecisionEngine.Manga
{
    // Sonarr divergence: NEW interface per Phase 5 D-05 — see DIVERGENCE.md.
    // Compile-error-driven additive contract (Phase 3 LEARNINGS pattern verbatim):
    // takes RemoteChapter not RemoteEpisode — TV specs cannot accidentally fire on a manga release.
    // Mirrors src/NzbDrone.Core/DecisionEngine/Specifications/IDownloadDecisionEngineSpecification.cs (13 lines).
    // Phase 8 collapses both interfaces when Tv/ deletes.
    //
    // Pitfall 6 mitigation: manga specs implement THIS interface ONLY. A class accidentally
    // implementing both IDownloadDecisionEngineSpecification AND IMangaDecisionEngineSpecification
    // would be auto-discovered into BOTH makers via DryIoc IEnumerable<TPlugin> — fails at runtime
    // with NRE on the wrong subject type. Plan acceptance grep guards against this.
    public interface IMangaDecisionEngineSpecification
    {
        RejectionType Type { get; }

        SpecificationPriority Priority { get; }

        DownloadSpecDecision IsSatisfiedBy(RemoteChapter subject, ReleaseDecisionInformation information);
    }
}
