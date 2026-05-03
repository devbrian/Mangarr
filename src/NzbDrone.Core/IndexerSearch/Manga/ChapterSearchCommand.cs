using System.Collections.Generic;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.IndexerSearch.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-12 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/IndexerSearch/EpisodeSearchCommand.cs.
    //
    // List<int> ChapterIds preserved (NOT a single id) — D-12 auto-retry
    // orchestrator pushes single-element lists per failed chapter, but the
    // collection shape mirrors EpisodeSearchCommand for symmetry and lets a
    // future bulk consumer reuse the same command without a second sibling.
    //
    // Phase 8 cleanup: collapse with EpisodeSearchCommand when Tv/ deletes.
    public class ChapterSearchCommand : Command
    {
        public List<int> ChapterIds { get; set; }

        public override bool SendUpdatesToClient => true;

        public ChapterSearchCommand()
        {
        }

        public ChapterSearchCommand(List<int> chapterIds)
        {
            ChapterIds = chapterIds;
        }
    }
}
