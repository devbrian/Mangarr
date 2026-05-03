using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Download;

namespace NzbDrone.Core.MediaFiles.MangaImport
{
    public interface IMakeMangaImportDecision
    {
        MangaImportDecision GetDecision(LocalChapter localChapter, DownloadClientItem downloadClientItem);
        List<MangaImportDecision> GetImportDecisions(List<LocalChapter> localChapters, DownloadClientItem downloadClientItem);
    }

    // Sonarr divergence: NEW manga sibling per Phase 6 PIPELINE-04 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/MediaFiles/EpisodeImport/ImportDecisionMaker.cs.
    //
    // Auto-discovers all IMangaImportDecisionEngineSpecification implementations via DryIoc
    // IEnumerable<> ctor injection (Phase 4 LEARNINGS S1 pattern). Manga simplifications vs.
    // TV ImportDecisionMaker:
    //   * No IDetectSample (no manga "sample" concept)
    //   * No IAggregationService (no manga scene-numbering / episode-aggregation)
    //   * No ITrackedDownloadService dependency at this layer (Plan 06-08 owns the tracked-
    //     download-to-LocalChapter mapping; the maker simply consumes already-resolved
    //     LocalChapter objects).
    //
    // Each spec is wrapped in try/catch so a single broken spec doesn't poison the whole
    // import batch — mirrors TV ImportDecisionMaker.EvaluateSpec at lines 185-205.
    //
    // Phase 8 cleanup: collapse with TV ImportDecisionMaker when Tv/ deletes.
    public class MangaImportDecisionMaker : IMakeMangaImportDecision
    {
        private readonly IEnumerable<IMangaImportDecisionEngineSpecification> _specifications;
        private readonly Logger _logger;

        public MangaImportDecisionMaker(IEnumerable<IMangaImportDecisionEngineSpecification> specifications, Logger logger)
        {
            _specifications = specifications;
            _logger = logger;
        }

        public MangaImportDecision GetDecision(LocalChapter localChapter, DownloadClientItem downloadClientItem)
        {
            var rejections = _specifications
                .Select(spec => Evaluate(spec, localChapter, downloadClientItem))
                .Where(r => r != null)
                .ToArray();

            return new MangaImportDecision(localChapter, rejections);
        }

        public List<MangaImportDecision> GetImportDecisions(List<LocalChapter> localChapters, DownloadClientItem downloadClientItem)
        {
            var decisions = new List<MangaImportDecision>();
            foreach (var lc in localChapters)
            {
                decisions.Add(GetDecision(lc, downloadClientItem));
            }

            return decisions;
        }

        private MangaImportRejection Evaluate(
            IMangaImportDecisionEngineSpecification spec,
            LocalChapter localChapter,
            DownloadClientItem downloadClientItem)
        {
            try
            {
                var result = spec.IsSatisfiedBy(localChapter, downloadClientItem);
                if (!result.Accepted)
                {
                    return new MangaImportRejection(result.Reason, result.Message);
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Couldn't evaluate decision on {0}", localChapter.Path);
                return new MangaImportRejection(
                    ImportRejectionReason.DecisionError,
                    $"{spec.GetType().Name}: {ex.Message}");
            }
        }
    }
}
