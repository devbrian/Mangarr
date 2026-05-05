using System;
using System.Collections.Generic;
using NLog;
using NzbDrone.Core.Parser.Manga.Model;

namespace NzbDrone.Core.DecisionEngine.Manga.Aggregators
{
    // Manga sibling of TV's RemoteEpisodeAggregationService
    // (src/NzbDrone.Core/Download/Aggregation/RemoteEpisodeAggregationService.cs).
    // Iterates registered IAggregateRemoteChapter augmenters in DryIoc-discovery
    // order, applies each to the RemoteChapter, and swallows per-augmenter
    // exceptions with a warn log so a single faulty augmenter cannot abort the
    // chain — verbatim TV catch-and-warn semantics.
    //
    // v1 has no concrete IAggregateRemoteChapter implementations registered yet
    // (Phase 8 backfill 14-02, audit gap
    // .planning/phases/08-tv-manga-parity-audit/audit/no-sibling/RemoteEpisodeAggregationService.md).
    // Consumer wiring into MangaDownloadDecisionMaker / TrackedDownloadService /
    // MangaQueueService is deferred to subsequent iterations per the plan's
    // "NO consumer wiring" constraint.
    public interface IRemoteChapterAggregationService
    {
        RemoteChapter Augment(RemoteChapter remoteChapter);
    }

    public class RemoteChapterAggregationService : IRemoteChapterAggregationService
    {
        private readonly IEnumerable<IAggregateRemoteChapter> _augmenters;
        private readonly Logger _logger;

        public RemoteChapterAggregationService(IEnumerable<IAggregateRemoteChapter> augmenters,
                                  Logger logger)
        {
            _augmenters = augmenters;
            _logger = logger;
        }

        public RemoteChapter Augment(RemoteChapter remoteChapter)
        {
            if (remoteChapter == null)
            {
                return null;
            }

            foreach (var augmenter in _augmenters)
            {
                try
                {
                    augmenter.Aggregate(remoteChapter);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, ex.Message);
                }
            }

            return remoteChapter;
        }
    }
}
