using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Manga;

namespace Mangarr.Api.V5.Manga.Release
{
    // Sonarr divergence: NEW manga sibling per debug session interactive-search-rejections (2026-05-08).
    // Role-match analog: src/Sonarr.Api.V5/Release/ReleaseDecisionResource.cs (deleted by Phase 15-10
    // commit d0b67fdf3 along with the rest of the V5 TV Release/ directory).
    //
    // Ports the canonical Sonarr V5 nested-decision wire shape so the frontend
    // useReleases.ts `Decision` interface (frontend/src/InteractiveSearch/useReleases.ts:100-105)
    // can read `release.decision.rejections.findIndex(r => r.reason === 'blocklisted')` without
    // crashing. The deleted V3 / pre-Phase-15 V5 ReleaseResource was flat (top-level
    // Approved/Rejected/Rejections-as-strings); the upstream Sonarr commit 9b756df4b ("Add v5
    // release endpoints") moved it under a nested Decision wrapper with structured rejections.
    //
    // Phase 8 cleanup: collapse with the unified ReleaseDecisionResource when Tv/ deletes.
    public class ReleaseDecisionResource
    {
        public bool Approved { get; set; }
        public bool TemporarilyRejected { get; set; }
        public bool Rejected { get; set; }
        public IEnumerable<DownloadRejectionResource> Rejections { get; set; } = new List<DownloadRejectionResource>();

        public ReleaseDecisionResource()
        {
        }

        public ReleaseDecisionResource(MangaDownloadDecision decision)
        {
            Approved = decision.Approved;
            TemporarilyRejected = decision.TemporarilyRejected;
            Rejected = decision.Rejected;
            Rejections = decision.Rejections.Select(r => new DownloadRejectionResource(r)).ToList();
        }
    }

    // Mirrors DownloadRejection (NzbDrone.Core.DecisionEngine.DownloadRejection). The frontend
    // typings/Rejection.ts contract is `{ message: string; reason: string; type: 'permanent' | 'temporary' }`.
    // System.Text.Json serializes the enum as a number by default; the frontend reads `reason` as a
    // string when filtering (`r.reason === 'blocklisted'`) — the `JsonStringEnumConverter` registered
    // globally in the V5 host emits enum names as strings, matching the frontend contract.
    public class DownloadRejectionResource
    {
        public string Message { get; set; } = string.Empty;
        public DownloadRejectionReason Reason { get; set; }
        public RejectionType Type { get; set; }

        public DownloadRejectionResource()
        {
        }

        public DownloadRejectionResource(DownloadRejection rejection)
        {
            Message = rejection.Message ?? string.Empty;
            Reason = rejection.Reason;
            Type = rejection.Type;
        }
    }
}
