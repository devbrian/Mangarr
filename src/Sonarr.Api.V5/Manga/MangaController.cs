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
    IHandle<ChapterListUpdatedEvent>
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

    protected override MangaResource? GetResourceById(int id)
    {
        var manga = _mangaService.GetManga(id);
        return MapResource(manga);
    }

    [RestPostById]
    [Consumes("application/json")]
    [Produces("application/json")]
    public Results<Created<MangaResource>, NotFound> AddManga([FromBody] MangaResource resource)
    {
        var manga = resource.ToModel();
        var added = _addMangaService.AddManga(manga!);

        return TypedCreated(added.Id);
    }

    [RestPutById]
    [Consumes("application/json")]
    [Produces("application/json")]
    public Results<Accepted<MangaResource>, NotFound> UpdateManga([FromBody] MangaResource resource)
    {
        var existing = _mangaService.GetManga(resource.Id);
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
}
