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
using Sonarr.Api.V5.Manga.Subresources;
using Sonarr.Http;
using Sonarr.Http.Extensions;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Manga.Wanted
{
    // Sonarr divergence: NEW manga V5 controller per Phase 12 Plan 12-12 (sub-wave-B-addition #4 —
    // F-CUTOFF closure). Phase-12 follow-up (F-CUTOFF-SIGNALR closure, 2026-05-06) refactored the
    // base class from plain `Controller` → `RestControllerWithSignalR<MangaCutoffResource, Chapter>`
    // and added IHandle subscriptions so the `/manga/wanted/cutoffunmet` page auto-refreshes on
    // chapter/chapterfile pipeline events end-to-end (matching TV's `/wanted/cutoffunmet` behavior).
    // See DIVERGENCE.md.
    //
    // Role-match analog (filing): src/Sonarr.Api.V5/Manga/Wanted/MissingChaptersController.cs
    // (Plan 06-09 sibling — still plain Controller, NOT refactored as part of this follow-up).
    // TV peer reference (cutoff-endpoint semantics + SignalR shape):
    //   src/Sonarr.Api.V5/Wanted/CutoffController.cs:15-63 (extends EpisodeControllerWithSignalR
    //   which itself extends RestControllerWithSignalR<EpisodeResource, Episode> + subscribes to
    //   EpisodeGrabbedEvent / EpisodeImportedEvent / EpisodeFileDeletedEvent).
    //
    // No `ChapterControllerWithSignalR` peer base class exists in v1 (verified via Grep
    // 2026-05-06). The manga ChapterController (Phase 7 Plan 07-01) likewise extends
    // RestControllerWithSignalR<TResource, TModel> directly without an intermediate base.
    // Creating an intermediate manga base for one consumer is over-engineered; matching the
    // existing manga ChapterController shape is the more honest mirror. Phase 8/15 cleanup may
    // introduce a `ChapterControllerWithSignalR` peer base if a second consumer (e.g., a future
    // MissingChaptersController refactor) emerges.
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
    // Manga sibling preserves (from MissingChaptersController): [V5ApiController] route attribute
    // (literal string per Plan 07-02 URL-shaped key contract); PagingRequestResource shape;
    // monitored filter; FilterExpressions chain; sort keys (releaseDate, chapterNumber, title);
    // optional MangaSubresource lazy hydration via includeManga query.
    //
    // Manga sibling diverges from MissingChaptersController:
    //   * Inject IChapterCutoffService (not IChapterService) — different paged query.
    //   * Drop the languages[] + ageRating filters (cutoff is profile-driven; languages + ageRating
    //     are upstream of the cutoff calculation per ChapterCutoffService:63-103). Keep monitored +
    //     mangaIds[] + includeManga.
    //   * Drop the post-paged in-memory ageRating filter (no ageRating query in this endpoint).
    //   * Extends RestControllerWithSignalR<MangaCutoffResource, Chapter> + subscribes to
    //     ChapterGrabbedEvent / ChapterImportedEvent / ChapterFileDeletedEvent (NOT plain
    //     Controller). MissingChaptersController is intentionally left plain by this follow-up —
    //     Phase 12 / 13+ will decide whether to apply the same SignalR refactor there.
    //
    // Manga sibling diverges from TV's CutoffController:
    //   * Extends RestControllerWithSignalR<MangaCutoffResource, Chapter> directly (TV extends
    //     EpisodeControllerWithSignalR — no manga peer base class exists; see top-of-file note).
    //   * No CutoffSubresource enum (TV's CutoffController accepts includeSubresources to lazy-hydrate
    //     Series/EpisodeFile/Images on the EpisodeResource); the manga peer surfaces a fixed
    //     MangaCutoffResource shape with optional MangaSubresource via the includeManga bool.
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
    // Phase 8 cleanup: collapse with TV's CutoffController (rename + flatten to src/Sonarr.Api.V5/Wanted/CutoffController.cs
    // post-Tv-namespace-delete) when Tv/ deletes.
    [V5ApiController("manga/wanted/cutoff")]
    public class MangaCutoffController : RestControllerWithSignalR<MangaCutoffResource, NzbDrone.Core.Manga.Chapter>,
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
        public Ok<PagingResource<MangaCutoffResource>> GetCutoffUnmetChapters([FromQuery] PagingRequestResource paging,
                                                                              bool monitored = true,
                                                                              [FromQuery] int[]? mangaIds = null,
                                                                              [FromQuery] bool includeManga = false)
        {
            var pagingResource = new PagingResource<MangaCutoffResource>(paging);
            var pagingSpec = pagingResource.MapToPagingSpec<MangaCutoffResource, NzbDrone.Core.Manga.Chapter>(
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

            // D-04: NO IsSynthetic filter (mirrors MissingChaptersController). Synthetic rows
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
        protected override MangaCutoffResource GetResourceById(int id)
        {
            var chapter = _chapterService.GetChapter(id);
            return MapToResource(chapter, includeManga: false);
        }

        private MangaCutoffResource MapToResource(NzbDrone.Core.Manga.Chapter chapter, bool includeManga)
        {
            var resource = chapter.ToCutoffResource()!;

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
        // round-trips through BroadcastResourceChange → GetResourceById → MangaCutoffResource so
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
