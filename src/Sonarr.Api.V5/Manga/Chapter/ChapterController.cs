using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.IndexerSearch.Manga;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.SignalR;
using Sonarr.Http;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;

namespace Sonarr.Api.V5.Manga.Chapter;

// Sonarr divergence: NEW manga V5 controller per Phase 7 D-07 — see DIVERGENCE.md.
// Role-match analog: src/Sonarr.Api.V5/Episodes/EpisodeController.cs (lines 14-86).
//
// API-01 + UI-04: per-chapter monitor toggle + manual search to the Phase 7 React
// Manga Details page.
//
// Manga sibling preserves:
//   * GET filtered by parent id (mangaId vs seriesId).
//   * Bulk monitor PUT shape ({ ChapterIds, Monitored } body).
//   * RestPutById single-row monitor flip.
//   * RestControllerWithSignalR<TResource, TModel> + IHandle<TUpdatedEvent> for live
//     UI updates.
//
// Manga sibling diverges from EpisodeController:
//   * IChapterService instead of IEpisodeService.
//   * No seasonNumber filter (PROJECT.md "Volumes/Seasons" Out-of-Scope).
//   * No EpisodeFile / Series subresource (deferred until a real consumer needs it).
//   * Search endpoint dispatches ChapterSearchCommand (Phase 6 D-12) via
//     IManageCommandQueue — there is no equivalent EpisodeController endpoint; the
//     sibling adds POST /api/v5/chapter/{id}/search per D-07.
//   * Bare [V5ApiController] (NOT [V5ApiController("chapter")]) so the SignalR resource
//     name auto-derives from ChapterResource.ResourceName = "chapter" (Pitfall 2 +
//     Lock #6). The HTTP route auto-lowercases [controller] → "chapter" via attribute
//     routing convention.
//   * Every HTTP method carries [Produces("application/json")] (Pitfall 4 — guarantees
//     OpenAPI v5 doc generation surfaces the response shape).
//
// Phase 8 cleanup: collapse with EpisodeController when Tv/ deletes.
[V5ApiController]
public class ChapterController : RestControllerWithSignalR<ChapterResource, NzbDrone.Core.Manga.Chapter>,
                                  IHandle<ChapterUpdatedEvent>
{
    private readonly IChapterService _chapterService;
    private readonly IManageCommandQueue _commandQueueManager;

    public ChapterController(IChapterService chapterService,
                             IManageCommandQueue commandQueueManager,
                             IBroadcastSignalRMessage signalRBroadcaster)
        : base(signalRBroadcaster)
    {
        _chapterService = chapterService;
        _commandQueueManager = commandQueueManager;
    }

    [HttpGet]
    [Produces("application/json")]
    public Results<Ok<List<ChapterResource>>, BadRequest> GetChapters(
        int? mangaId,
        [FromQuery] List<int> chapterIds)
    {
        if (mangaId.HasValue)
        {
            return TypedResults.Ok(_chapterService.GetChaptersByManga(mangaId.Value).ToResource());
        }

        if (chapterIds is { Count: > 0 })
        {
            return TypedResults.Ok(_chapterService.GetChapters(chapterIds).ToResource());
        }

        // T-07-01 mitigation: no parent id and no chapterIds → 400, mirrors
        // EpisodeController.GetEpisodes precedent (RESEARCH §Security V5).
        throw new BadRequestException("mangaId or chapterIds must be provided");
    }

    protected override ChapterResource? GetResourceById(int id)
    {
        var chapter = _chapterService.GetChapter(id);
        if (chapter == null)
        {
            // BL-01 mirror: explicit NotFoundException so the framework maps to 404
            // instead of returning HTTP 200 with a null body (matches MangaController
            // GetResourceById precedent at MangaController.cs:76-85).
            throw new NotFoundException();
        }

        return chapter.ToResource();
    }

    [RestPutById]
    [Consumes("application/json")]
    [Produces("application/json")]
    public Results<Ok<ChapterResource>, NotFound> SetChapterMonitored([FromRoute] int id, [FromBody] ChapterResource resource)
    {
        // WR-09 fix: ChapterService.SetChapterMonitored has the BL-03 cascade-delete
        // null-guard and silently returns when the row was just removed. Without a
        // matching null-check here, the subsequent GetChapter(id) returns null and
        // the .ToResource() extension throws NRE → 500 to the client. Mirrors the
        // GetResourceById precedent above which uses NotFoundException for the same
        // case; the typed-results NotFound shape preserves the controller signature
        // for OpenAPI v5 doc generation (Pitfall 4).
        _chapterService.SetChapterMonitored(id, resource.Monitored);
        var updated = _chapterService.GetChapter(id);
        if (updated == null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(updated.ToResource());
    }

    [HttpPut("monitor")]
    [Consumes("application/json")]
    [Produces("application/json")]
    public Ok<List<ChapterResource>> SetChaptersMonitored([FromBody] ChaptersMonitoredResource resource)
    {
        // Mirrors EpisodeController.SetEpisodesMonitored: single id → SetChapterMonitored
        // (which handles the BL-03 cascade-delete null-guard); many ids → bulk path. The
        // bulk path's ChapterUpdatedEvent fan-out (Plan 07-01 Task 1) drives the SignalR
        // `chapter` push so every affected row refreshes on the React detail page.
        if (resource.ChapterIds.Count == 1)
        {
            _chapterService.SetChapterMonitored(resource.ChapterIds.First(), resource.Monitored);
        }
        else
        {
            _chapterService.SetChaptersMonitored(resource.ChapterIds, resource.Monitored);
        }

        return TypedResults.Ok(_chapterService.GetChapters(resource.ChapterIds).ToResource());
    }

    [HttpPost("{id:int}/search")]
    [Produces("application/json")]
    public Accepted<int> SearchChapter([FromRoute] int id)
    {
        // D-07: per-chapter manual search dispatches a single-element ChapterSearchCommand
        // (Phase 6 D-12 — the bulk shape is preserved even for the per-chapter path so a
        // future bulk consumer can reuse the same command without a second sibling).
        // T-07-04 acceptance: IManageCommandQueue.Push deduplicates by command equality;
        // a flood of identical single-id searches collapses to one queued command.
        var command = new ChapterSearchCommand(new List<int> { id });
        var queued = _commandQueueManager.Push(command, CommandPriority.Normal, CommandTrigger.Manual);
        return TypedResults.Accepted((string?)null, queued.Id);
    }

    [NonAction]
    public void Handle(ChapterUpdatedEvent message)
    {
        // SignalR fan-out: ChapterService publishes ChapterUpdatedEvent on every
        // monitor flip (Plan 07-01 Task 1). Resource name auto-derives to "chapter"
        // from ChapterResource.ResourceName because [V5ApiController] is bare.
        BroadcastResourceChange(ModelAction.Updated, message.Chapter.Id);
    }
}
