using Mangarr.Api.V5.Manga.Chapter;
using Mangarr.Api.V5.Manga.Subresources;
using Mangarr.Http;
using Mangarr.Http.Extensions;
using Mangarr.Http.REST;
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

namespace Mangarr.Api.V5.Manga.Wanted
{
    // Sonarr divergence: NEW manga V5 controller per Phase 12 Plan 12-12 (sub-wave-B-addition #4 —
    // F-CUTOFF closure). Phase-12 follow-up (F-CUTOFF-SIGNALR closure, 2026-05-06) refactored the
    // base class from plain `Controller` → `RestControllerWithSignalR<MangaCutoffResource, Chapter>`
    // and added IHandle subscriptions so the `/manga/wanted/cutoffunmet` page auto-refreshes on
    // chapter/chapterfile pipeline events end-to-end (matching TV's `/wanted/cutoffunmet` behavior).
    // See DIVERGENCE.md.
    //
    // Phase-12 follow-up (canonical-resource-reuse, 2026-05-06) refactored the resource shape
    // from custom `MangaCutoffResource` (deleted) → canonical `ChapterResource` reuse, mirroring
    // TV's `CutoffController` returning `Ok<PagingResource<EpisodeResource>>` rather than its
    // own custom resource. The query param `bool includeManga` was likewise replaced with TV's
    // `[FromQuery] MangaCutoffSubresource[]? includeSubresources` enum-array shape (see
    // `MangaCutoffSubresource.cs` — single `Manga` value mirrors TV's `CutoffSubresource
    // { Series, EpisodeFile, Images }`). Closes the canonical-resource-reuse divergence.
    //
    // Role-match analog (filing): src/Mangarr.Api.V5/Manga/Wanted/MangaMissingController.cs
    // (Plan 06-09 sibling — also extends RestControllerWithSignalR<,> after the F-MISSING-SIGNALR
    // follow-up landed the parallel refactor 2026-05-06, mirroring this controller's shape).
    // TV peer reference (cutoff-endpoint semantics + SignalR shape):
    //   src/Mangarr.Api.V5/Wanted/CutoffController.cs:15-63 (extends EpisodeControllerWithSignalR
    //   which itself extends RestControllerWithSignalR<EpisodeResource, Episode> + subscribes to
    //   EpisodeGrabbedEvent / EpisodeImportedEvent / EpisodeFileDeletedEvent).
    //
    // No `ChapterControllerWithSignalR` peer base class exists in v1 (verified via Grep
    // 2026-05-06). The manga ChapterController (Phase 7 Plan 07-01) likewise extends
    // RestControllerWithSignalR<TResource, TModel> directly without an intermediate base.
    // Creating an intermediate manga base for one consumer is over-engineered; matching the
    // existing manga ChapterController shape is the more honest mirror. After F-MISSING-SIGNALR
    // landed (MangaMissingController also extends RestControllerWithSignalR<,> directly), the
    // pair pattern is now: two manga V5 wanted controllers extending the foundational base
    // directly. Phase 15 cleanup may introduce a `ChapterControllerWithSignalR` peer base when
    // collapsing with the TV side, since TV's MissingController + CutoffController share the
    // EpisodeControllerWithSignalR intermediate.
    //
    // Trigger: F-CUTOFF surfaced by Plan 12-99 Task 7 Playwright walkthrough on 2026-05-06 — the
    // frontend route /manga/wanted/cutoffunmet (shipped by Plan 12-08) renders without errors but
    // every fetch to GET /api/v5/manga/wanted/cutoff returned 404 because this controller did not
    // exist. Plan 12-12 shipped the controller in-phase; this follow-up wires the SignalR emission
    // end-to-end so the page actually auto-refreshes on backend pipeline events.
    //
    // Backend service-layer dependency: IChapterCutoffService.ChaptersWhereCutoffUnmet
    // (src/NzbDrone.Core/Manga/ChapterCutoffService.cs:10-104 — Phase 8 audit no-sibling/EpisodeCutoffService.md).
    // The service computes belowCutoff translation + custom-format profile id sets, then delegates
    // the paged query to ChapterRepository.ChaptersWhereCutoffUnmet (src/NzbDrone.Core/Manga/ChapterRepository.cs:88-90).
    //
    // Manga sibling preserves (from MangaMissingController): [V5ApiController] route attribute
    // (literal string per Plan 07-02 URL-shaped key contract); PagingRequestResource shape;
    // monitored filter; FilterExpressions chain; sort keys (releaseDate, chapterNumber, title);
    // optional MangaSubresource lazy hydration via the includeSubresources enum-array.
    //
    // Manga sibling diverges from MangaMissingController:
    //   * Inject IChapterCutoffService (not IChapterService) — different paged query.
    //   * Drop the ageRating filter (cutoff is profile-driven; ageRating is upstream of the
    //     cutoff calculation per ChapterCutoffService:63-103). Keep monitored + mangaIds[] +
    //     includeSubresources.
    //   * Drop the post-paged in-memory ageRating filter (no ageRating query in this endpoint).
    //   * Extends RestControllerWithSignalR<ChapterResource, Chapter> + subscribes to
    //     ChapterGrabbedEvent / ChapterImportedEvent / ChapterFileDeletedEvent (NOT plain
    //     Controller). MangaMissingController has the same shape after the F-MISSING-SIGNALR
    //     follow-up landed (commit 6c587de3d) for cross-controller consistency.
    //
    // Manga sibling diverges from TV's CutoffController:
    //   * Extends RestControllerWithSignalR<ChapterResource, Chapter> directly (TV extends
    //     EpisodeControllerWithSignalR — no manga peer base class exists; see top-of-file note).
    //   * `MangaCutoffSubresource` enum has a single `Manga` value (TV's `CutoffSubresource` has
    //     `{ Series, EpisodeFile, Images }`). The ChapterFile subresource on ChapterResource is
    //     the `ChapterFileId` scalar (not a nested resource) and chapter-level images are deferred
    //     per Plan 06-09 rationale — so neither analog applies in v1.
    //
    // SignalR emission contract (mirrors TV EpisodeControllerWithSignalR shape verbatim):
    //   * `manga/wanted/cutoff` resource name auto-derives from [V5ApiController("manga/wanted/cutoff")]
    //     via RestControllerWithSignalR.cs:23-33 (apiAttribute.Resource read).
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
    // Phase 8 cleanup: collapse with TV's CutoffController (rename + flatten to src/Mangarr.Api.V5/Wanted/CutoffController.cs
    // post-Tv-namespace-delete) when Tv/ deletes.
    [V5ApiController("manga/wanted/cutoff")]
    public class MangaCutoffController : RestControllerWithSignalR<ChapterResource, NzbDrone.Core.Manga.Chapter>,
                                         IHandle<ChapterGrabbedEvent>,
                                         IHandle<ChapterImportedEvent>,
                                         IHandle<ChapterFileDeletedEvent>
    {
        private readonly IChapterCutoffService _chapterCutoffService;
        private readonly IChapterService _chapterService;
        private readonly IMangaService _mangaService;

        public MangaCutoffController(IChapterCutoffService chapterCutoffService,
                                     IChapterService chapterService,
                                     IMangaService mangaService,
                                     IBroadcastSignalRMessage signalRBroadcaster)
            : base(signalRBroadcaster)
        {
            _chapterCutoffService = chapterCutoffService;
            _chapterService = chapterService;
            _mangaService = mangaService;
        }

        [HttpGet]
        [Produces("application/json")]
        public Ok<PagingResource<ChapterResource>> GetCutoffUnmetChapters([FromQuery] PagingRequestResource paging,
                                                                          bool monitored = true,
                                                                          [FromQuery] int[]? mangaIds = null,
                                                                          [FromQuery] MangaCutoffSubresource[]? includeSubresources = null)
        {
            var includeManga = includeSubresources?.Contains(MangaCutoffSubresource.Manga) ?? false;

            // Sonarr-canonical sort keys: firstReleaseDate (Sonarr-mirror of Episode.AirDateUtc),
            // chapterNumber, title. Frontend sends `?sortKey=firstReleaseDate` directly.
            var pagingResource = new PagingResource<ChapterResource>(paging);
            var pagingSpec = pagingResource.MapToPagingSpec<ChapterResource, NzbDrone.Core.Manga.Chapter>(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "firstReleaseDate",
                    "chapterNumber",
                    "title"
                },
                "firstReleaseDate",
                SortDirection.Ascending);

            if (monitored)
            {
                pagingSpec.FilterExpressions.Add(c => c.Monitored == true);
            }

            if (mangaIds != null && mangaIds.Length > 0)
            {
                pagingSpec.FilterExpressions.Add(c => mangaIds.Contains(c.MangaId));
            }

            // D-04: NO IsSynthetic filter (mirrors MangaMissingController). Synthetic rows
            // surface alongside real-feed rows by default. Indexer match upgrades the synthetic
            // row in place per Phase 2 D-17.

            var page = pagingSpec.ApplyToPage(
                spec => _chapterCutoffService.ChaptersWhereCutoffUnmet(spec),
                c => MapToResource(c, includeManga));

            return TypedResults.Ok(page);
        }

        // RestControllerWithSignalR.BroadcastResourceChange(ModelAction, int) round-trips through
        // GetResourceById to materialize the resource for the SignalR Body. Default base impl
        // throws — provide the manga-shape lookup so IHandle subscribers can broadcast by id.
        // Mirrors EpisodeControllerWithSignalR.GetResourceById at EpisodeControllerWithSignalR.cs:53-58.
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

        // F-CUTOFF-SIGNALR follow-up (2026-05-06): mirrors TV
        // EpisodeControllerWithSignalR.Handle(EpisodeGrabbedEvent) at lines 122-132. RemoteChapter
        // carries the resolved Chapters list (Parser/Manga/Model/RemoteChapter.cs:32). Each id
        // round-trips through BroadcastResourceChange → GetResourceById → ChapterResource so
        // the React Query cache for ['/manga/wanted/cutoff'] sees a fresh row per chapter on grab.
        [NonAction]
        public void Handle(ChapterGrabbedEvent message)
        {
            foreach (var chapter in message.RemoteChapter.Chapters)
            {
                BroadcastResourceChange(ModelAction.Updated, chapter.Id);
            }
        }

        // F-CUTOFF-SIGNALR follow-up (2026-05-06): mirrors TV
        // EpisodeControllerWithSignalR.Handle(EpisodeImportedEvent) at lines 134-141. Manga
        // ChapterImportedEvent carries a single Chapter (NOT a list) — the manga import pipeline
        // imports one chapter per event per Plan 06-07 Pitfall 4 (publish AFTER ChapterFile DB
        // commit + filesystem move complete).
        [NonAction]
        public void Handle(ChapterImportedEvent message)
        {
            BroadcastResourceChange(ModelAction.Updated, message.Chapter.Id);
        }

        // F-CUTOFF-SIGNALR follow-up (2026-05-06): mirrors TV
        // EpisodeControllerWithSignalR.Handle(EpisodeFileDeletedEvent) at lines 143-150 PLUS the
        // Upgrade-reason short-circuit from MangaController.Handle(ChapterFileDeletedEvent) at
        // MangaController.cs:336-341 (the upcoming Add event fires next, so broadcasting twice
        // for one logical change is wasteful). ChapterFile carries a single ChapterId.
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
