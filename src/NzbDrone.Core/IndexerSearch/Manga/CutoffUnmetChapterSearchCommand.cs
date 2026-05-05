using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.IndexerSearch.Manga
{
    // Sonarr divergence: NEW manga sibling per Phase 8 audit gap-04 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/IndexerSearch/CutoffUnmetEpisodeSearchCommand.cs.
    //
    // Wanted/CutoffUnmet sweep — searches monitored chapters whose imported file is
    // below the user's CustomFormatProfile / TranslationProfile cutoff (per
    // IChapterCutoffService.ChaptersWhereCutoffUnmet). Mirrors TV's
    // CutoffUnmetEpisodeSearchCommand shape (`int? MangaId`, `bool Monitored`,
    // `SendUpdatesToClient => true`); Monitored defaults to true.
    //
    // Phase 8 cleanup: collapse with CutoffUnmetEpisodeSearchCommand when Tv/ deletes.
    public class CutoffUnmetChapterSearchCommand : Command
    {
        public int? MangaId { get; set; }
        public bool Monitored { get; set; }

        public override bool SendUpdatesToClient => true;

        public CutoffUnmetChapterSearchCommand()
        {
            Monitored = true;
        }

        public CutoffUnmetChapterSearchCommand(int mangaId)
        {
            MangaId = mangaId;
            Monitored = true;
        }
    }
}
