using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.SignalR;
using Sonarr.Http;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;

namespace Sonarr.Api.V5.Manga;

// Phase 2 developer-surface CRUD controller per Plan 02-10. Mirrors Sonarr's
// SeriesController shape (Sonarr.Api.V5/Series/SeriesController.cs) but slimmed for
// the Phase 2 manga domain: no path validators (root-folder validation is Phase 7
// territory), no SeriesStats analog (chapter stats arrive in Phase 6), no scene-mapping
// (manga has no scene numbering). [V5ApiController] attribute carries the admin
// X-Api-Key requirement per RESEARCH §Security Domain V4.
[V5ApiController]
public class MangaController : RestControllerWithSignalR<MangaResource, NzbDrone.Core.Manga.Manga>,
    IHandle<MangaAddedEvent>,
    IHandle<MangaUpdatedEvent>,
    IHandle<MangaDeletedEvent>,
    IHandle<ChapterListUpdatedEvent>,
    IHandle<MangaCoversUpdatedEvent>     // NEW per Plan 09-13 (audit gap-03 SignalR consumer)
{
    private readonly IMangaService _mangaService;
    private readonly IAddMangaService _addMangaService;
    private readonly IMapMangaCoversToLocal _coverMapper;

    public MangaController(IBroadcastSignalRMessage signalRBroadcaster,
                           IMangaService mangaService,
                           IAddMangaService addMangaService,
                           IMapMangaCoversToLocal coverMapper)
        : base(signalRBroadcaster)
    {
        _mangaService = mangaService;
        _addMangaService = addMangaService;
        _coverMapper = coverMapper;

        SharedValidator.RuleFor(m => m.Title).NotEmpty();

        // POST requires at least one cross-source ID — AddMangaService dedup +
        // CrossSourceIdResolver both key off MangaDex/MAL/AniList IDs (Plan 02-09).
        PostValidator.RuleFor(m => m).Must(r =>
                r.MangaDexId.HasValue || r.MalId.HasValue || r.AniListId.HasValue)
            .WithMessage("At least one of MangaDexId / MalId / AniListId must be set");
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<List<MangaResource>> GetAll()
    {
        var all = _mangaService.GetAllManga();
        var result = new List<MangaResource>(all.Count);

        foreach (var m in all)
        {
            var resource = MapResource(m);

            if (resource != null)
            {
                result.Add(resource);
            }
        }

        return TypedResults.Ok(result);
    }

    // WR-09 fix: explicit NotFoundException on missing id so the framework maps to
    // 404 instead of returning HTTP 200 with a null body. Mirrors SeriesController
    // precedent.
    protected override MangaResource? GetResourceById(int id)
    {
        var manga = _mangaService.GetManga(id);
        if (manga == null)
        {
            throw new NotFoundException();
        }

        return MapResource(manga);
    }

    [RestPostById]
    [Consumes("application/json")]
    [Produces("application/json")]
    public Results<Created<MangaResource>, NotFound, Conflict<string>> AddManga([FromBody] MangaResource resource)
    {
        try
        {
            var manga = resource.ToModel();
            var added = _addMangaService.AddManga(manga!);

            // Hand-construct the 201 instead of TypedCreated(int) — the union here
            // includes Conflict<string>, which the base helper's signature does not.
            var addedResource = MapResource(added);
            return TypedResults.Created(
                Url.Action(nameof(GetResourceByIdWithErrorHandler), new { id = added.Id }),
                addedResource);
        }
        catch (InvalidOperationException ex)
        {
            // WR-10 fix: AddMangaService throws InvalidOperationException on dedup
            // collisions ("Manga with MangaDex ID ... already exists"). Map to 409
            // Conflict instead of 500.
            return TypedResults.Conflict(ex.Message);
        }
    }

    [RestPutById]
    [Consumes("application/json")]
    [Produces("application/json")]
    public Results<Accepted<MangaResource>, NotFound> UpdateManga([FromBody] MangaResource resource)
    {
        // BL-01 fix: GetManga now returns null on missing (was throwing
        // ModelNotFoundException upstream). Surface as 404 instead of NRE / 500.
        var existing = _mangaService.GetManga(resource.Id);
        if (existing == null)
        {
            return TypedResults.NotFound();
        }

        existing.ApplyChanges(resource.ToModel()!);
        _mangaService.UpdateManga(existing);

        return TypedAccepted(resource.Id);
    }

    [RestDeleteById]
    public NoContent DeleteManga(int id, bool deleteFiles = false)
    {
        _mangaService.DeleteManga(new List<int> { id }, deleteFiles);

        return TypedResults.NoContent();
    }

    private MangaResource? MapResource(NzbDrone.Core.Manga.Manga? manga)
    {
        if (manga == null)
        {
            return null;
        }

        var resource = manga.ToResource();

        if (resource?.Images != null)
        {
            _coverMapper.ConvertToLocalUrls(manga.Id, resource.Images);
        }

        return resource;
    }

    [NonAction]
    public void Handle(MangaAddedEvent message)
    {
        BroadcastResourceChange(ModelAction.Created, message.Manga.Id);
    }

    [NonAction]
    public void Handle(MangaUpdatedEvent message)
    {
        BroadcastResourceChange(ModelAction.Updated, message.Manga.Id);
    }

    [NonAction]
    public void Handle(MangaDeletedEvent message)
    {
        var resource = MapResource(message.Manga);

        if (resource == null)
        {
            return;
        }

        BroadcastResourceChange(ModelAction.Deleted, resource);
    }

    [NonAction]
    public void Handle(ChapterListUpdatedEvent message)
    {
        BroadcastResourceChange(ModelAction.Updated, message.Manga.Id);
    }

    // Phase 9 Plan 09-13 (sub-wave A 09-04 audit gap-03 close-out): SignalR consumer for the
    // new MangaCoversUpdatedEvent published by MangaMediaCoverService.HandleAsync(MangaUpdatedEvent).
    // Mirrors SeriesController.cs:415-421 verbatim shape — only broadcasts when Updated == true
    // (Pitfall 4 contract — the event was published AFTER disk writes completed, so the
    // downstream re-fetch reads fresh thumbnail data from /MediaCover/manga/{id}/...).
    [NonAction]
    public void Handle(MangaCoversUpdatedEvent message)
    {
        if (message.Updated)
        {
            BroadcastResourceChange(ModelAction.Updated, message.Manga.Id);
        }
    }
}
