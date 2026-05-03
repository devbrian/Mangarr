using NLog;
using NzbDrone.Core.Parser.Manga.Model;

namespace NzbDrone.Core.DecisionEngine.Manga.Specifications
{
    // Sonarr divergence: NEW manga sibling per Phase 5 D-06 — see DIVERGENCE.md.
    // Role-match analog: TV QueueSpecification at
    // src/NzbDrone.Core/DecisionEngine/Specifications/QueueSpecification.cs (115 lines).
    //
    // Phase 5 STUB — queue model parity: the existing IQueueService queue holds Queue rows whose
    // .RemoteEpisode is a TV RemoteEpisode (Queue.cs:36) and whose .Episodes is a List<Episode>
    // (TV-only). A manga sibling Queue model + IMangaQueueService is a Phase 6 deliverable.
    // Without the manga queue, this spec has no way to safely intersect by Chapter.Id (Episode.Id
    // and Chapter.Id share int Id space, so iterating the TV queue would produce false positives
    // when an episode's id happens to match a chapter's id).
    //
    // Ships as Accept-always so the auto-discovery 11-spec count + DI-resolution test in plan
    // 05-07 pass. Implements IMangaDecisionEngineSpecification ONLY (Pitfall 6 guard preserved);
    // priority = Default (matches TV QueueSpecification); type = Permanent.
    //
    // TODO Phase 6 — wire manga queue overload (IMangaQueueService.GetMangaQueue() returning
    // List<MangaQueueItem> with .RemoteChapter property) when 06-CONTEXT.md is authored. The
    // wired body should iterate _mangaQueueService.GetMangaQueue() and reject when any queued
    // item shares a Chapter.Id with subject.Chapters; SIMPLIFIED — no upgrade arithmetic in
    // Phase 5 (manga has no quality model). Phase 6 owns "is queued release worse than candidate"
    // upgrade decision.
    // Phase 8 cleanup: collapse with TV analog when Tv/ deletes.
    public class QueueDuplicateSpecification : IMangaDecisionEngineSpecification
    {
        private readonly Logger _logger;

        public QueueDuplicateSpecification(Logger logger)
        {
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public DownloadSpecDecision IsSatisfiedBy(RemoteChapter subject, ReleaseDecisionInformation information)
        {
            // TODO Phase 6 — wire IMangaQueueService manga queue overload.
            return DownloadSpecDecision.Accept();
        }
    }
}
