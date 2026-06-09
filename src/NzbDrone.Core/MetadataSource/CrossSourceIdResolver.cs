using System;
using System.Collections.Generic;
using System.Linq;
using F23.StringSimilarity;
using NLog;
using NzbDrone.Core.Parser.Manga;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Lightweight POCO bag of the inputs <see cref="CrossSourceIdResolver.TryResolve"/>
    /// needs from any provider's response. Lets the resolver stay decoupled from
    /// <see cref="NzbDrone.Core.Manga.Manga"/> persistence concerns — callers project
    /// their provider DTOs into this shape before calling <c>TryResolve</c>.
    /// </summary>
    public class MangaCandidate
    {
        public IList<string> AllTitles { get; init; } = new List<string>();
        public int? PublicationYear { get; init; }
        public string PrimaryAuthor { get; init; }
        public int? TotalChapterCount { get; init; }
    }

    /// <summary>
    /// Cross-source ID resolution gate per Phase 2 D-19..D-22.
    ///
    /// <para>
    /// PER PITFALL 5 (RESEARCH §Pitfall 5): F23.StringSimilarity's
    /// <c>JaroWinkler.Similarity</c> returns HIGHER values for MORE similar strings
    /// (1.0 = identical, 0.0 = completely different). NEVER call
    /// <c>JaroWinkler.Distance</c> here — the threshold direction would invert and
    /// every cross-source link would silently fail (or pass) the gate.
    /// </para>
    ///
    /// <para>
    /// PER D-21: TryResolve enforces TWO gates:
    /// (a) Title gate — Jaro-Winkler max-similarity across the cartesian product of
    ///     normalized primary + secondary titles must be ≥ 0.85.
    /// (b) Multi-axis confirm — at least 2 of 3 supporting axes must agree:
    ///     PublicationYear within ±1, PrimaryAuthor case-insensitive equality after
    ///     normalization, TotalChapterCount within 10%.
    /// Both gates must pass; otherwise the link is logged "unresolved" and the
    /// caller leaves the cross-source ID null per D-23.
    /// </para>
    ///
    /// <para>
    /// PER D-05: Title and author normalization MUST go through
    /// <see cref="MangaTitleNormalizer.Normalize"/> — the single source of truth for
    /// title canonicalization shared with <c>AddMangaService</c> dedup. Keeping the
    /// algorithm in one place prevents drift between the two consumers.
    /// </para>
    /// </summary>
    public class CrossSourceIdResolver
    {
        // PER PITFALL 5: ALWAYS use .Similarity (HIGHER = more similar). NEVER .Distance.
        // Documented threshold direction: ≥0.85 means MORE similar.
        private static readonly JaroWinkler JaroWinkler = new();
        public const double SimilarityThreshold = 0.85;     // D-21 first half
        public const int MinAxisAgreement = 2;              // D-21 second half (≥2 of 3)

        private readonly Logger _logger;

        public CrossSourceIdResolver(Logger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Raw match signals for a (primary, secondary) candidate pair — the numbers BEHIND
        /// the <see cref="TryResolve"/> pass/fail gate. Exposed so callers that must choose
        /// among SEVERAL gate-clearing candidates can rank by match STRENGTH rather than take
        /// the first passer in arbitrary provider order. The motivating case: a metadata
        /// source (e.g. MangaBaka) returns BOTH an authoritative record and sparse same-title
        /// duplicate stubs for one series; without a strength signal the auto-relink picked
        /// whichever the provider happened to list first.
        /// </summary>
        public readonly struct MangaMatchScore
        {
            /// <summary>Max Jaro-Winkler similarity across the normalized title cartesian product (HIGHER = more similar).</summary>
            public double TitleSimilarity { get; init; }

            /// <summary>Count of confirmed supporting axes (0..3): publication-year ±1, primary-author exact, chapter-count within 10%.</summary>
            public int ConfirmedAxes { get; init; }

            /// <summary>True when BOTH gates pass (title ≥ threshold AND axes ≥ min-agreement) — i.e. <see cref="TryResolve"/> would return true.</summary>
            public bool Passed => TitleSimilarity >= SimilarityThreshold && ConfirmedAxes >= MinAxisAgreement;
        }

        /// <summary>
        /// Pure scorer (no logging, no side effects) computing the title similarity + confirmed
        /// axis count for a candidate pair. <see cref="TryResolve"/> delegates here for its
        /// pass/fail decision; ranking callers reuse it to compare candidates.
        /// </summary>
        public MangaMatchScore Score(MangaCandidate primary, MangaCandidate secondary)
        {
            // Title gate — normalize ALL titles via single source of truth (D-05) before similarity.
            var primaryTitles = primary.AllTitles?
                .Select(MangaTitleNormalizer.Normalize)
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList() ?? new List<string>();
            var secondaryTitles = secondary.AllTitles?
                .Select(MangaTitleNormalizer.Normalize)
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList() ?? new List<string>();

            var maxSim = 0.0;
            foreach (var p in primaryTitles)
            {
                foreach (var s in secondaryTitles)
                {
                    var sim = JaroWinkler.Similarity(p, s);   // HIGHER = more similar (Pitfall 5)
                    if (sim > maxSim)
                    {
                        maxSim = sim;
                    }
                }
            }

            // Multi-axis confirm — count agreeing axes (TryResolve needs ≥2 of 3).
            var axes = 0;
            if (primary.PublicationYear.HasValue && secondary.PublicationYear.HasValue
                && Math.Abs(primary.PublicationYear.Value - secondary.PublicationYear.Value) <= 1)
            {
                axes++;
            }

            if (!string.IsNullOrEmpty(primary.PrimaryAuthor) && !string.IsNullOrEmpty(secondary.PrimaryAuthor)
                && string.Equals(MangaTitleNormalizer.Normalize(primary.PrimaryAuthor),
                                 MangaTitleNormalizer.Normalize(secondary.PrimaryAuthor),
                                 StringComparison.Ordinal))
            {
                axes++;
            }

            // WR-05 fix: percentage-of-max instead of percentage-of-primary, which
            // is symmetric and correctly handles the (much more common) case where
            // the secondary reports 0 because the manga is just-launched. Pre-fix
            // gate was `<= primary * 0.10` which always failed when secondary == 0.
            // D-21 says "within 10%" without specifying direction, so symmetric is
            // the right reading. We also explicitly require BOTH counts to be > 0
            // before crediting the axis — a 0 on either side is "no signal" not
            // "perfect mismatch."
            if (primary.TotalChapterCount.HasValue && secondary.TotalChapterCount.HasValue
                && primary.TotalChapterCount.Value > 0
                && secondary.TotalChapterCount.Value > 0)
            {
                var maxCount = Math.Max(primary.TotalChapterCount.Value, secondary.TotalChapterCount.Value);
                var delta = Math.Abs(primary.TotalChapterCount.Value - secondary.TotalChapterCount.Value);
                if (delta <= maxCount * 0.10)
                {
                    axes++;
                }
            }

            return new MangaMatchScore { TitleSimilarity = maxSim, ConfirmedAxes = axes };
        }

        /// <summary>
        /// Per D-21: Title gate (≥0.85 Jaro-Winkler max-similarity across normalized titles)
        /// AND ≥2-of-3 multi-axis confirm (publication-year ±1, primary-author exact,
        /// total-chapter-count within 10%). Returns false + populates <paramref name="reason"/>
        /// if either gate fails (logged as "unresolved" per D-21).
        /// </summary>
        public bool TryResolve(MangaCandidate primary, MangaCandidate secondary, out string reason)
        {
            var score = Score(primary, secondary);

            if (score.TitleSimilarity < SimilarityThreshold)
            {
                reason = $"title-similarity {score.TitleSimilarity:F2} < {SimilarityThreshold:F2}";
                _logger.Debug("CrossSource unresolved: {0}", reason);
                return false;
            }

            if (score.ConfirmedAxes < MinAxisAgreement)
            {
                reason = $"only {score.ConfirmedAxes} of 3 axes confirmed (need >= {MinAxisAgreement})";
                _logger.Debug("CrossSource unresolved: {0}", reason);
                return false;
            }

            reason = $"sim={score.TitleSimilarity:F2} axes={score.ConfirmedAxes}/3";
            _logger.Trace("CrossSource resolved: {0}", reason);
            return true;
        }
    }
}
