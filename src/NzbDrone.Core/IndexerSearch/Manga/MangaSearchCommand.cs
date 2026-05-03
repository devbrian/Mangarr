using System.Collections.Generic;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.IndexerSearch.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-06 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/IndexerSearch/SeriesSearchCommand.cs.
    //
    // D-06: bulk MangaIds (NOT per-chapter fan-out). MissingChapterSearchService
    // groups missing chapters by MangaId and pushes one MangaSearchCommand per
    // affected Manga; commands serialize through IManageCommandQueue so the
    // per-source rate budget (Phase 1 D-11) is naturally respected.
    //
    // Phase 8 cleanup: collapse with SeriesSearchCommand when Tv/ deletes.
    public class MangaSearchCommand : Command
    {
        public List<int> MangaIds { get; set; }
        public bool UserInvokedSearch { get; set; }

        public override bool SendUpdatesToClient => true;

        public MangaSearchCommand()
        {
        }

        public MangaSearchCommand(List<int> mangaIds, bool userInvoked = false)
        {
            MangaIds = mangaIds;
            UserInvokedSearch = userInvoked;
        }
    }
}
