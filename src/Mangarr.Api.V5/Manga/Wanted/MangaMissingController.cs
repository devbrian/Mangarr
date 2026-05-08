using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.MediaFiles.MangaImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.SignalR;
using Mangarr.Api.V5.Manga.Chapter;
using Mangarr.Api.V5.Manga.Subresources;
using Sonarr.Http;
using Sonarr.Http.Extensions;
using Sonarr.Http.REST;

namespace Mangarr.Api.V5.Manga.Wanted
{
    // Sonarr divergence: NEW manga V5 controller per Phase 6 Plan 06-09 — see DIVERGENCE.md.
    // Phase-12 follow-up (F-MISSING-SIGNALR closure, 2026-05-06) refactored the base class from
    // plain `Controller` → `RestControllerWithSignalR<MissingChapterResource, Chapter>` and
    // added IHandle subscriptions so the `/manga/wanted/missing` page auto-refreshes on
    // chapter/chapterfile pipeline events end-to-end (matching TV's `/wanted/missing` behavior
    // and mirroring the F-CUTOFF-SIGNALR closure verbatim — see MangaCutoffController.cs).
    //
    // Phase-12 follow-up (canonical-resource-reuse, 2026-05-06) refactored the resource shape
    // from custom `MissingChapterResource` (deleted) → canonical `ChapterResource` reuse,
    // mirroring TV's `MissingController` returning `Ok<PagingResource<EpisodeResource>>` rather
    // than its own custom resource. The query param `bool includeManga` was likewise replaced
    // with TV's `[FromQuery] MangaMissingSubresource[]? includeSubresources` enum-array shape
    // (see `MangaMissingSubresource.cs` — single `Manga` value mirrors TV's `MissingSubresource
    // { Series, Images }`). Closes the canonical-resource-reuse divergence.
    //
    // Role-match analog: src/Mangarr.Api.V5/Wanted/MissingController.cs (TV peer — extends
    // EpisodeControllerWithSignalR which itself extends RestControllerWithSignalR<EpisodeResource,
    // Episode>; manga peer extends RestControllerWithSignalR<,> directly because no
    // ChapterControllerWithSignalR base class exists in v1 — verified via Grep 2026-05-06).
    //
    // WANTED-01..03: paged list of monitored chapters that have no ChapterFile imported,
    // filterable by mangaIds + languages + ageRating.
    //
    // Manga sibling preserves: [V5ApiController] route attribute (literal string per Plan 07-02
    // URL-shaped key contract); PagingRequestResource shape; monitored filter; FilterExpressions
    // chain.
    //
    // Manga sibling diverges from MissingController:
    //   * Inject IChapterService (paged ChaptersWithoutFiles overload added by Plan 06-09)
    //     + IMangaService (for ageRating filter join + Manga subresource hydration).
    //   * Drop EpisodesWithoutFiles + includeSpecials (manga has no specials concept).
    //   * Add mangaIds + languages + ageRating filters per WANTED-03.
    //   * D-04 honored: IsSynthetic=true rows are surfaced identically to IsSynthetic=false
    //     rows by default. No `WHERE IsSynthetic = false` filter is applied. A future
    //     `excludeSynthetic` query param could opt-in to the filter; v1 default is INCLUDE.
    //   * ageRating filter is post-paged (in-memory) because it requires a JOIN to Manga
    //     and the v1 ChapterRepository.ChaptersWithoutFiles does not JOIN. Acceptable for
    //     Phase 6 — the result set is bounded by the paging spec (default 10/page).
    //   * Extends RestControllerWithSignalR<ChapterResource, Chapter> + subscribes to
    //     ChapterGrabbedEvent / ChapterImportedEvent / ChapterFileDeletedEvent (NOT plain
    //     Controller). F-MISSING-SIGNALR follow-up (2026-05-06) — mirrors the F-CUTOFF-SIGNALR
    //     closure for cross-controller consistency between the two manga V5 wanted endpoints.
    //
    // SignalR emission contract (mirrors TV EpisodeControllerWithSignalR shape verbatim):
    //   * `manga/wanted/missing` resource name auto-derives from
    //     [V5ApiController("manga/wanted/missing")] via RestControllerWithSignalR.cs:23-33.
    //   * IHandle<ChapterGrabbedEvent> → broadcasts ModelAction.Updated for each chapter id in the
    //     event's RemoteChapter.Chapters list (mirrors TV EpisodeGrabbedEvent fan-out at
    //     EpisodeControllerWithSignalR.cs:122-132).
    //   * IHandle<ChapterImportedEvent> → broadcasts ModelAction.Updated for the imported Chapter's
    //     Id (mirrors TV EpisodeImportedEvent fan-out at EpisodeControllerWithSignalR.cs:134-141 —
    //     manga ChapterImportedEvent carries a single Chapter, not a list, because the manga import
    //     pipeline imports one chapter per event per Plan 06-07 Pitfall 4).
    //   * IHandle<ChapterFileDeletedEvent> → broadcasts ModelAction.Updated for the deleted file's
    //     ChapterId (mirrors TV EpisodeFileDeletedEvent fan-out at EpisodeControllerWithSignalR.cs:143-150).
    //   * Upgrade-reason short-circuit on ChapterFileDeletedEvent mirrors MangaController's
    //     ChapterFileDeletedEvent handler (MangaController.cs:336-341) — the upcoming Add event
    //     fires next, so broadcasting twice for one logical change is wasteful.
    //
    // Phase 8 cleanup: collapse with MissingController when Tv/ deletes.
    [V5ApiController("manga/wanted/missing")]
    public class MangaMissingController : RestControllerWithSignalR<ChapterResource, NzbDrone.Core.Manga.Chapter>,
                                             IHandle<ChapterGrabbedEvent>,
                                             IHandle<ChapterImportedEvent>,
                                             IHandle<ChapterFileDeletedEvent>
    {
        private readonly IChapterService _chapterService;
        private readonly IMangaService _mangaService;

        public MangaMissingController(IChapterService chapterService,
                                         IMangaService mangaService,
                                         IBroadcastSignalRMessage signalRBroadcaster)
            : base(signalRBroadcaster)
        {
            _chapterService = chapterService;
            _mangaService = mangaService;
        }

        [HttpGet]
        [Produces("application/json")]
        public Ok<PagingResource<ChapterResource>> GetMissingChapters([FromQuery] PagingRequestResource paging,
                                                                      bool monitored = true,
                                                                      [FromQuery] int[]? mangaIds = null,
                                                                      [FromQuery] string[]? languages = null,
                                                                      [FromQuery] string? ageRating = null,
                                                                      [FromQuery] MangaMissingSubresource[]? includeSubresources = null)
        {
            var includeManga = includeSubresources?.Contains(MangaMissingSubresource.Manga) ?? false;

            var pagingResource = new PagingResource<ChapterResource>(paging);
            var pagingSpec = pagingResource.MapToPagingSpec<ChapterResource, NzbDrone.Core.Manga.Chapter>(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "releaseDate",
                    "chapterNumber",
                    "title"
                },
                "releaseDate",
                SortDirection.Ascending);

            if (monitored)
            {
                pagingSpec.FilterExpressions.Add(c => c.Monitored == true);
            }

            if (mangaIds != null && mangaIds.Length > 0)
            {
                pagingSpec.FilterExpressions.Add(c => mangaIds.Contains(c.MangaId));
            }

            if (languages != null && languages.Length > 0)
            {
                pagingSpec.FilterExpressions.Add(c => languages.Contains(c.TranslatedLanguage));
            }

            // D-04: NO IsSynthetic filter. Synthetic rows are placeholder rows from the
            // metadata-only-count fallback (Phase 2 D-17) and surface identically to real-feed
            // rows in the Wanted list. Indexer match upgrades the synthetic row in place per
            // Phase 2 D-17.

            // ageRating requires a Manga JOIN; apply post-paged in-memory.
            // The result set is bounded by the paging spec (default 10/page) so the
            // in-memory pass cost is negligible. Future Phase 8 collapse may push the
            // filter into the SQL builder.
            var page = pagingSpec.ApplyToPage(
                spec => _chapterService.ChaptersWithoutFiles(spec),
                c => MapToResource(c, includeManga));

            if (!string.IsNullOrWhiteSpace(ageRating))
            {
                var mangaIdsInPage = page.Records.Select(r => r.MangaId).Distinct().ToList();
                var mangaLookup = _mangaService.GetManga(mangaIdsInPage)
                    .ToDictionary(m => m.Id, m => m.ContentRating);
                page.Records = page.Records
                    .Where(r => mangaLookup.TryGetValue(r.MangaId, out var rating)
                                && string.Equals(rating, ageRating, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            return TypedResults.Ok(page);
        }

        // RestControllerWithSignalR.BroadcastResourceChange(ModelAction, int) round-trips through
        // GetResourceById to materialize the resource for the SignalR Body. Default base impl
        // throws — provide the manga-shape lookup so IHandle subscribers can broadcast by id.
        // Mirrors EpisodeControllerWithSignalR.GetResourceById at EpisodeControllerWithSignalR.cs:53-58
        // and MangaCutoffController.GetResourceById (F-CUTOFF-SIGNALR sibling, 2026-05-06).
        // After canonical-resource-reuse follow-up (2026-05-06): returns ChapterResource via the
        // canonical ChapterResourceMapper.ToResource extension (no Manga subresource hydration on
        // the broadcast path — TV's EpisodeControllerWithSignalR.GetResourceById hydrates Series
        // for the broadcast, but the manga-side default is `false` for parity with the original
        // shape; the broadcast Body's Id is the load-bearing field for updatePagedItem<Episode>).
        protected override ChapterResource GetResourceById(int id)
        {
            var chapter = _chapterService.GetChapter(id);
            return MapToResource(chapter, includeManga: false);
        }

        private ChapterResource MapToResource(NzbDrone.Core.Manga.Chapter chapter, bool includeManga)
        {
            var resource = chapter.ToResource();

            if (includeManga)
            {
                var manga = _mangaService.GetManga(chapter.MangaId);
                if (manga != null)
                {
                    resource.Manga = new MangaSubresource
                    {
                        Id = manga.Id,
                        Title = manga.Title
                    };
                }
            }

            return resource;
        }

        // F-MISSING-SIGNALR follow-up (2026-05-06): mirrors TV
        // EpisodeControllerWithSignalR.Handle(EpisodeGrabbedEvent) at lines 122-132 +
        // MangaCutoffController.Handle(ChapterGrabbedEvent) sibling. RemoteChapter carries the
        // resolved Chapters list (Parser/Manga/Model/RemoteChapter.cs:32). Each id round-trips
        // through BroadcastResourceChange → GetResourceById → ChapterResource so the
        // React Query cache for ['/manga/wanted/missing'] sees a fresh row per chapter on grab.
        [NonAction]
        public void Handle(ChapterGrabbedEvent message)
        {
            foreach (var chapter in message.RemoteChapter.Chapters)
            {
                BroadcastResourceChange(ModelAction.Updated, chapter.Id);
            }
        }

        // F-MISSING-SIGNALR follow-up (2026-05-06): mirrors TV
        // EpisodeControllerWithSignalR.Handle(EpisodeImportedEvent) at lines 134-141 +
        // MangaCutoffController.Handle(ChapterImportedEvent) sibling. Manga ChapterImportedEvent
        // carries a single Chapter (NOT a list) — the manga import pipeline imports one chapter
        // per event per Plan 06-07 Pitfall 4 (publish AFTER ChapterFile DB commit + filesystem
        // move complete).
        [NonAction]
        public void Handle(ChapterImportedEvent message)
        {
            BroadcastResourceChange(ModelAction.Updated, message.Chapter.Id);
        }

        // F-MISSING-SIGNALR follow-up (2026-05-06): mirrors TV
        // EpisodeControllerWithSignalR.Handle(EpisodeFileDeletedEvent) at lines 143-150 PLUS the
        // Upgrade-reason short-circuit from MangaController.Handle(ChapterFileDeletedEvent) at
        // MangaController.cs:336-341 (the upcoming Add event fires next, so broadcasting twice
        // for one logical change is wasteful) + MangaCutoffController.Handle(ChapterFileDeletedEvent)
        // sibling. ChapterFile carries a single ChapterId.
        [NonAction]
        public void Handle(ChapterFileDeletedEvent message)
        {
            if (message.Reason == NzbDrone.Core.MediaFiles.DeleteMediaFileReason.Upgrade)
            {
                return;
            }

            BroadcastResourceChange(ModelAction.Updated, message.ChapterFile.ChapterId);
        }
    }
}
