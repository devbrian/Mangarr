using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Manga;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.SignalR;
using Sonarr.Http;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;
using BadRequestException = Sonarr.Http.REST.BadRequestException;

namespace Sonarr.Api.V5.Manga.Chapter;

// Sonarr divergence: NEW manga V5 controller per Phase 13 Plan 13-07
// (D-13-04 forward-prophylactic + D-13-07 Series-rename family rule). See DIVERGENCE.md.
// Role-match analog: src/Sonarr.Api.V5/EpisodeFiles/EpisodeFileController.cs.
//
// API-13 + PARITY-01 + PARITY-02: per-chapter-file CRUD + bulk delete + SignalR fan-out
// for the React `useEpisodeFiles.ts:21,46,69,93` peer (`useChapterFiles.ts` lands in a
// future plan; this controller backfills the backend ahead of demand per D-13-04).
//
// Manga sibling preserves:
//   * Bare [V5ApiController] auto-derives BOTH the HTTP route /api/v5/ChapterFile AND
//     the SignalR resource name `chapterfile` (Pitfall 2 + Lock #6). The SignalR resource
//     name is auto-derived from `new ChapterFileResource().ResourceName.Trim('/')` per
//     RestControllerWithSignalR.cs:23-33; RestResource.ResourceName.cs:11 returns
//     `GetType().Name.ToLowerInvariant().Replace("resource", "")` which yields the
//     all-lowercase `chapterfile` literal. The frontend SignalRListener.tsx handler
//     entry MUST match this lowercase string — see Plan 13-07 Task 3.
//   * RestControllerWithSignalR<TResource, TModel> + IHandle<ChapterFileAddedEvent> +
//     IHandle<ChapterFileDeletedEvent> for live UI updates (mirrors EpisodeFile peer).
//   * GET filtered by parent id (`mangaId` instead of `seriesId`).
//   * GET by ids (`chapterFileIds` instead of `episodeFileIds`).
//   * RestDeleteById single-row delete + HttpDelete("bulk") body-shaped delete.
//   * [Produces("application/json")] on every HTTP method (Pitfall 4 — guarantees
//     OpenAPI v5 doc generation surfaces the response shape).
//
// Manga sibling diverges from EpisodeFileController:
//   * IChapterFileService instead of IMediaFileService; IDeleteMediaFiles.DeleteChapterFile
//     instead of DeleteEpisodeFile (peer added to the existing interface in this plan).
//   * IMangaService instead of ISeriesService.
//   * NO ICustomFormatCalculationService / IUpgradableSpecification injections — the
//     ChapterFileResource mapper takes ONLY the ChapterFile (manga DTO has no
//     QualityModel? Quality / CustomFormatScore / QualityCutoffNotMet fields per
//     Phase 5 D-05; CF + Translation Profile cutoff lives on MangaCutoffController).
//   * NO PUT (single-row) endpoint — TV's `SetQuality` flips Quality / SceneName /
//     ReleaseGroup; manga has no Quality field and ReleaseGroup edits arrive via the
//     editor flow rather than the file controller. RestPutById omitted; if a future
//     consumer needs per-file metadata edits, add `[RestPutById]` SetMetadata mirroring
//     the TV shape.
//   * NO PUT bulk endpoint — same reasoning.
//
// Phase 8 cleanup: collapse with EpisodeFileController when Tv/ deletes.
[V5ApiController]
public class ChapterFileController : RestControllerWithSignalR<ChapterFileResource, ChapterFile>,
                                     IHandle<ChapterFileAddedEvent>,
                                     IHandle<ChapterFileDeletedEvent>
{
    private readonly IChapterFileService _chapterFileService;
    private readonly IDeleteMediaFiles _mediaFileDeletionService;
    private readonly IMangaService _mangaService;

    public ChapterFileController(IBroadcastSignalRMessage signalRBroadcaster,
                                 IChapterFileService chapterFileService,
                                 IDeleteMediaFiles mediaFileDeletionService,
                                 IMangaService mangaService)
        : base(signalRBroadcaster)
    {
        _chapterFileService = chapterFileService;
        _mediaFileDeletionService = mediaFileDeletionService;
        _mangaService = mangaService;
    }

    protected override ChapterFileResource GetResourceById(int id)
    {
        var chapterFile = _chapterFileService.Get(id);
        return chapterFile.ToResource();
    }

    [HttpGet]
    [Produces("application/json")]
    public Results<Ok<List<ChapterFileResource>>, BadRequest> GetChapterFiles(int? mangaId, [FromQuery] List<int>? chapterFileIds)
    {
        if (!mangaId.HasValue && chapterFileIds?.Any() == false)
        {
            // T-13-05 mitigation: neither parent id nor explicit id list → 400. Mirrors
            // EpisodeFileController.GetEpisodeFiles precedent so the front-end gets a
            // typed BadRequestException instead of an empty 200.
            throw new BadRequestException("mangaId or chapterFileIds must be provided");
        }

        if (mangaId.HasValue)
        {
            var files = _chapterFileService.GetFilesByManga(mangaId.Value);

            if (files == null)
            {
                return TypedResults.Ok(new List<ChapterFileResource>());
            }

            return TypedResults.Ok(files.ConvertAll(f => f.ToResource()));
        }
        else
        {
            var chapterFiles = _chapterFileService.Get(chapterFileIds!);
            return TypedResults.Ok(chapterFiles.ConvertAll(f => f.ToResource()));
        }
    }

    [RestDeleteById]
    public Results<NoContent, NotFound> DeleteChapterFile(int id)
    {
        var chapterFile = _chapterFileService.Get(id);

        if (chapterFile == null)
        {
            throw new NzbDroneClientException(HttpStatusCode.NotFound, "Chapter file not found");
        }

        var manga = _mangaService.GetManga(chapterFile.MangaId);

        // T-13-05 mitigation: paths come from ChapterFile model lookup (DB-stored normalized
        // paths via the relative-path + manga.Path Combine in DeleteChapterFile), NOT from
        // arbitrary user input — same shape as the TV peer's DeleteEpisodeFile.
        _mediaFileDeletionService.DeleteChapterFile(manga, chapterFile);

        return TypedResults.NoContent();
    }

    [HttpDelete("bulk")]
    [Consumes("application/json")]
    public NoContent DeleteChapterFiles([FromBody] ChapterFileListResource resource)
    {
        var chapterFiles = _chapterFileService.Get(resource.ChapterFileIds);

        if (chapterFiles.Count == 0)
        {
            return TypedResults.NoContent();
        }

        var manga = _mangaService.GetManga(chapterFiles.First().MangaId);

        foreach (var chapterFile in chapterFiles)
        {
            _mediaFileDeletionService.DeleteChapterFile(manga, chapterFile);
        }

        return TypedResults.NoContent();
    }

    [NonAction]
    public void Handle(ChapterFileAddedEvent message)
    {
        // SignalR fan-out: ChapterFileService.Add publishes ChapterFileAddedEvent after
        // row insert; the Updated broadcast lets the React /chapterfile page pick up newly
        // imported files without a manual refresh. Resource name auto-derives to
        // `chapterfile` from ChapterFileResource.ResourceName because [V5ApiController]
        // is bare. Mirrors EpisodeFileController.Handle(EpisodeFileAddedEvent).
        BroadcastResourceChange(ModelAction.Updated, message.ChapterFile.Id);
    }

    [NonAction]
    public void Handle(ChapterFileDeletedEvent message)
    {
        // SignalR fan-out: ChapterFileService.Delete publishes ChapterFileDeletedEvent
        // after row delete. Mirrors EpisodeFileController.Handle(EpisodeFileDeletedEvent)
        // verbatim — TV peer also broadcasts ModelAction.Deleted by the deleted file's id
        // (NOT by ChapterId — the resource being deleted is the ChapterFile itself).
        BroadcastResourceChange(ModelAction.Deleted, message.ChapterFile.Id);
    }
}
