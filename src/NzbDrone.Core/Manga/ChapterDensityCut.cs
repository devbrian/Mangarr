using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Manga
{
    // Sonarr divergence: NEW-in-Mangarr shared boundary math — no TV analog. The single
    // source of truth for the metadata-anchored density cut used in TWO places that must
    // never drift apart:
    //   * ChapterSynthesisService.ResolveDensityCut — PREVENTS new strays on-search/on-grab
    //     (anchored on the union of existing DB whole numbers + this batch's gateway numbers);
    //   * StrayChapterPruneService.Analyze — CLEANS UP strays a pre-fix reconciliation already
    //     materialized (anchored on WITH-FILE evidence — the on-disk numbers above the baseline).
    // Mirrored verbatim by scripts/audit-stray-chapters.py:density_cut.
    public static class ChapterDensityCut
    {
        // Minimum fraction of the post-metadata-baseline range that must be backed by actual
        // present chapter numbers for an extension top to be honored. A contiguous real run
        // scores ~1.0; a sparse smattering of mislabeled stray numbers scores far below this
        // and is cut back to the dense cluster. Tuned so the lowest realistic stray (a single
        // number ~2x the real count) lands well under it while genuine multi-source coverage
        // (even 60-90% dense) clears it comfortably.
        public const double MinPostBaselineDensity = 0.5;

        // Trust metadata's [1..baseline] verbatim, then scan the present numbers ABOVE the
        // baseline (up to candidateMax) high→low and return the largest top whose post-baseline
        // density |present in (baseline,top]| / (top-baseline) clears the floor. If no extension
        // clears it, fall back to the trusted baseline (bounded by candidateMax so we never
        // invent beyond what was actually observed). baseline 0 (no metadata count) makes the
        // formula degrade to the full-range density count/max.
        public static decimal Resolve(decimal baseline, decimal candidateMax, ISet<decimal> present)
        {
            if (baseline < 0)
            {
                baseline = 0;
            }

            if (present == null)
            {
                return Math.Min(baseline, candidateMax);
            }

            var candidates = present
                .Where(n => n > baseline && n <= candidateMax)
                .OrderByDescending(n => n);

            foreach (var top in candidates)
            {
                var span = top - baseline;          // > 0 by the Where filter above
                var inRange = present.Count(n => n > baseline && n <= top);
                var density = (double)inRange / (double)span;

                if (density >= MinPostBaselineDensity)
                {
                    return top;
                }
            }

            // Nothing beyond the baseline is dense enough — fall back to the trusted baseline.
            return Math.Min(baseline, candidateMax);
        }
    }
}
