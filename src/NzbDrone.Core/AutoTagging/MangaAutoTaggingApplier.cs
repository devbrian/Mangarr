using NLog;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.AutoTagging
{
    // Phase 24 v1.1 Wave 3 — 3-event applier per Open Q #5 + D-06 resolution.
    //
    // Routing:
    //   AutoTagsUpdatedEvent     -> full-library retroactive re-eval (D-06)
    //   MangaAddedEvent          -> per-manga eval on add
    //   MangaRefreshCompleteEvent -> full-library re-eval on refresh complete
    //
    // The applier reuses AutoTaggingService.GetTagChanges verbatim (24-02 restore;
    // Pitfall 3 anti-rewrite gate); this class adds ZERO algorithm code -- only
    // event routing + manga.Tags mutation + UpdateManga(publishUpdatedEvent: false)
    // suppression to prevent event flood during bulk re-tag.
    //
    // D-05 user-applied-tags-sticky guarantee is preserved because GetTagChanges's
    // TagsToRemove enumerates only THIS rule's tags (when RemoveTagsAutomatically=true);
    // user-applied tags that no rule references stay untouched by construction.
    //
    // DryIoc auto-discovery picks up IHandle<> subscribers via RegisterMany
    // convention (Extensions.cs:25-35); no manual DI registration required.
    public class MangaAutoTaggingApplier : IHandle<AutoTagsUpdatedEvent>,
                                            IHandle<MangaAddedEvent>,
                                            IHandle<MangaRefreshCompleteEvent>
    {
        private readonly IAutoTaggingService _autoTaggingService;
        private readonly IMangaService _mangaService;
        private readonly Logger _logger;

        public MangaAutoTaggingApplier(IAutoTaggingService autoTaggingService,
                                       IMangaService mangaService,
                                       Logger logger)
        {
            _autoTaggingService = autoTaggingService;
            _mangaService = mangaService;
            _logger = logger;
        }

        public void Handle(AutoTagsUpdatedEvent message)
        {
            // D-06 retroactive — re-evaluate full library on rule save (Insert /
            // Update / Delete all fire AutoTagsUpdatedEvent from AutoTaggingService).
            foreach (var manga in _mangaService.GetAllManga())
            {
                ApplyChanges(manga);
            }
        }

        public void Handle(MangaAddedEvent message)
        {
            ApplyChanges(message.Manga);
        }

        public void Handle(MangaRefreshCompleteEvent message)
        {
            // D-06 symmetric trigger — refresh completion re-evaluates the full
            // library. MangaRefreshCompleteEvent is parameterless (verified in 24-02
            // restore baseline); applier re-evaluates ALL mangas. Acceptable cost per
            // Deferred Idea #6 / RESEARCH A6; library size <5K typical.
            foreach (var manga in _mangaService.GetAllManga())
            {
                ApplyChanges(manga);
            }
        }

        private void ApplyChanges(Manga.Manga manga)
        {
            var changes = _autoTaggingService.GetTagChanges(manga);

            if (changes.TagsToAdd.Count == 0 && changes.TagsToRemove.Count == 0)
            {
                return;
            }

            foreach (var tag in changes.TagsToAdd)
            {
                manga.Tags.Add(tag);
            }

            foreach (var tag in changes.TagsToRemove)
            {
                manga.Tags.Remove(tag);
            }

            // Phase 10-07 2-arg overload — publishUpdatedEvent: false suppresses
            // MangaUpdatedEvent flood during bulk re-tag. D-06 rationale.
            _mangaService.UpdateManga(manga, publishUpdatedEvent: false);

            _logger.Debug("AutoTagging applied to {0}: +{1} -{2}",
                          manga.Title,
                          changes.TagsToAdd.Count,
                          changes.TagsToRemove.Count);
        }
    }
}
