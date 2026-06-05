using System.Collections.Generic;
using NzbDrone.Core.DecisionEngine.Manga;
using NzbDrone.Core.Parser.Manga.Model;

namespace NzbDrone.Core.Manga
{
    // Sonarr divergence: NEW-in-Mangarr — no TV analog. Sonarr never synthesizes
    // catalog rows from a download source because TheTVDB defines both "what exists"
    // and "what's grabbable". Mangarr's external gateway exposes chapter releases the
    // MangaDex metadata catalog never enumerated, so the local Chapter catalog must be
    // reconciled against the gateway. See DIVERGENCE.md (Phase 40). Auto-registered by
    // DryIoc convention (I{Name}+{Name} pair) — no manual Startup registration.
    public interface IChapterSynthesisService
    {
        // On-search: backfill the genuinely-missing contiguous WHOLE-number range
        // [0..maxWhole] for the searched manga from the attribution-gated decisions.
        // Fractionals are NEVER bulk-synthesized on search (D-02).
        void SynthesizeFromDecisions(Manga manga, List<MangaDownloadDecision> decisions);

        // On-grab: synthesize the single grabbed number (whole OR fractional, D-04)
        // when no local Chapter row exists for it.
        void SynthesizeForGrab(RemoteChapter remoteChapter);
    }
}
