using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.IndexerSearch.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 6 D-09 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/IndexerSearch/MissingEpisodeSearchCommand.cs.
    //
    // D-09: walks all monitored Mangas (or one if MangaId set), groups missing
    // chapters by MangaId, and pushes ONE MangaSearchCommand per affected
    // Manga (NOT per-chapter fan-out). Per-source rate budget naturally
    // respected because commands serialize through IManageCommandQueue.
    //
    // Phase 8 cleanup: collapse with MissingEpisodeSearchCommand when Tv/ deletes.
    public class MissingChapterSearchCommand : Command
    {
        public int? MangaId { get; set; }
        public bool Monitored { get; set; }

        public override bool SendUpdatesToClient => true;

        public MissingChapterSearchCommand()
        {
            Monitored = true;
        }

        public MissingChapterSearchCommand(int mangaId)
        {
            MangaId = mangaId;
            Monitored = true;
        }
    }
}
