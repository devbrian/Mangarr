using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
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
    IHandle<MangaCoversUpdatedEvent>,    // Plan 09-13 (audit gap-03 SignalR consumer)
    IHandle<MangaEditedEvent>,           // Plan 10-05 (Phase 10 sub-wave A FINDINGS gap_in_scope close-out — UI bulk-edit modal close + UI single-edit save after Plan 10-07 dual-publish)
    IHandle<MangaRenamedEvent>,          // Plan 10-05 (FINDINGS gap_in_scope close-out — UI rename re-render after RenameChapterFileService completes)
    IHandle<MangaBulkEditedEvent>,       // Plan 10-05 (FINDINGS gap_in_scope close-out — UI bulk-edit fan-out for each manga in payload)
    IHandle<ChapterFileAddedEvent>,      // Plan 10-06 (FINDINGS gap_in_scope close-out — UI library page re-fetch on chapter-file arrival; mirrors SeriesController.IHandle<EpisodeImportedEvent>)
    IHandle<ChapterFileDeletedEvent>,    // Plan 10-06 (FINDINGS gap_in_scope close-out — UI library page re-fetch on chapter-file deletion; mirrors SeriesController.IHandle<EpisodeFileDeletedEvent>)
    IHandle<MangaImportedEvent>          // Plan 10-08 (FINDINGS gap_in_scope close-out — UI library page re-fetch on bulk-add-complete; mirrors SeriesController.IHandle<EpisodeImportedEvent>)
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

        // Phase 10 Plan 10-07 (W4 revision iteration 1 — Option B locked per D-10-08):
        // opt the UI single-edit PUT path INTO BOTH MangaUpdatedEvent + MangaEditedEvent
        // so MangaController.IHandle<MangaEditedEvent> (Plan 10-05) fires on the
        // user-explicit-edit signal and MangaController.IHandle<MangaUpdatedEvent>
        // fires on the any-update signal. Without this opt-in, the IHandle subscriber
        // for MangaEditedEvent would never fire on the UI single-edit path — the
        // half-broken intermediate state D-10-08 explicitly prohibits.
        _mangaService.UpdateManga(existing, publishUpdatedEvent: true, triggerSeriesEdited: true);

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

    // Phase 10 Plan 10-05 (sub-wave A FINDINGS gap_in_scope close-out): SignalR consumer for
    // MangaEditedEvent published by MangaService.UpdateManga (bulk-edit path; UI single-edit
    // path adds a dual-publish in Plan 10-07). Mirrors SeriesController.cs Handle(SeriesEditedEvent)
    // shape — broadcasts Updated for the manga.Id so the UI list re-fetches after the
    // user-explicit edit (vs metadata-refresh-driven MangaUpdatedEvent path).
    [NonAction]
    public void Handle(MangaEditedEvent message)
    {
        BroadcastResourceChange(ModelAction.Updated, message.Manga.Id);
    }

    // Phase 10 Plan 10-05 (sub-wave A FINDINGS gap_in_scope close-out): SignalR consumer for
    // MangaRenamedEvent published by RenameChapterFileService at src/NzbDrone.Core/MediaFiles/RenameChapterFileService.cs
    // (Plan 02-10 ordering invariant — DB write FIRST, event LAST). Mirrors SeriesController.cs
    // Handle(SeriesRenamedEvent) shape verbatim — UI re-renders manga details + chapter list after
    // file rename completes.
    [NonAction]
    public void Handle(MangaRenamedEvent message)
    {
        BroadcastResourceChange(ModelAction.Updated, message.Manga.Id);
    }

    // Phase 10 Plan 10-05 (sub-wave A FINDINGS gap_in_scope close-out): SignalR consumer for
    // MangaBulkEditedEvent published by MangaService bulk-edit path (Phase 8 audit
    // gap-01 SeriesService-vs-MangaService.md). Mirrors SeriesController.cs
    // Handle(SeriesBulkEditedEvent) shape — broadcasts Updated for EACH manga in the
    // bulk payload so the UI library list re-fetches the affected rows.
    [NonAction]
    public void Handle(MangaBulkEditedEvent message)
    {
        foreach (var manga in message.Manga)
        {
            BroadcastResourceChange(ModelAction.Updated, manga.Id);
        }
    }

    // Phase 10 Plan 10-06 (FINDINGS gap_in_scope close-out — EpisodeFileAddedEvent-vs-ChapterFileAddedEvent
    // pair report subscriber-set asymmetry): SignalR consumer for ChapterFileAddedEvent published by
    // ChapterFileService.Add (RESEARCH §7 line 482). Mirrors SeriesController.Handle(EpisodeImportedEvent)
    // shape — broadcasts Updated for the manga.Id so the UI library page re-fetches the row's
    // chapter-file count after the import completes (Pitfall 4 contract — DB write happened FIRST in
    // ChapterFileService.Add, this event publishes LAST).
    //
    // Manga has no separate ChapterFileController in v1 (PROJECT.md flat-chapter design); the SignalR
    // push reuses the `manga` resource name. Frontend SignalRListener.tsx `name === 'manga'` handler
    // (Plan 07-02) invalidates the ['/manga'] query key — UI library page re-fetches.
    //
    // mangaId-extraction path: ChapterFile.MangaId direct property (verified
    // src/NzbDrone.Core/MediaFiles/ChapterFile.cs:13). No LazyLoad navigation needed.
    [NonAction]
    public void Handle(ChapterFileAddedEvent message)
    {
        BroadcastResourceChange(ModelAction.Updated, message.ChapterFile.MangaId);
    }

    // Phase 10 Plan 10-06 (FINDINGS gap_in_scope close-out — EpisodeFileDeletedEvent-vs-ChapterFileDeletedEvent
    // pair report subscriber-set asymmetry): SignalR consumer for ChapterFileDeletedEvent published by
    // ChapterFileService.Delete (RESEARCH §7 line 483). Mirrors SeriesController.Handle(EpisodeFileDeletedEvent)
    // shape — broadcasts Updated for the manga.Id so the UI library page re-fetches the chapter-file count
    // after the deletion completes.
    //
    // Mirrors TV's Upgrade-reason short-circuit: SeriesController bails when Reason == Upgrade because
    // an upgrade replaces one file with another and the Add event will fire next; broadcasting twice for
    // the same logical change is wasteful. Manga adopts the same short-circuit verbatim.
    //
    // mangaId-extraction path: ChapterFile.MangaId direct property (verified
    // src/NzbDrone.Core/MediaFiles/ChapterFile.cs:13).
    [NonAction]
    public void Handle(ChapterFileDeletedEvent message)
    {
        if (message.Reason == DeleteMediaFileReason.Upgrade)
        {
            return;
        }

        BroadcastResourceChange(ModelAction.Updated, message.ChapterFile.MangaId);
    }

    // Phase 10 Plan 10-08 (FINDINGS gap_in_scope close-out — SeriesImportedEvent → MangaImportedEvent
    // subscriber-set asymmetry on MangaController vs SeriesController.IHandle<EpisodeImportedEvent>):
    // SignalR consumer for MangaImportedEvent published by MangaService.AddManga(List<Manga>) bulk-add
    // path (RESEARCH §7 line 474-475). Mirrors SeriesController.Handle(EpisodeImportedEvent) shape —
    // broadcasts Updated for EACH manga.Id in the bulk payload so the UI library page re-fetches the
    // affected rows after Phase 7 AddManga library-import flow completes.
    //
    // Note: MangaAddedHandler at src/NzbDrone.Core/Manga/MangaAddedHandler.cs:35 ALSO subscribes to
    // this event for refresh-trigger PushMany. DryIoc auto-discovery dispatches both subscribers in
    // parallel — no conflict (this controller does SignalR fan-out only; the handler does command-queue
    // work only).
    [NonAction]
    public void Handle(MangaImportedEvent message)
    {
        foreach (var mangaId in message.MangaIds)
        {
            BroadcastResourceChange(ModelAction.Updated, mangaId);
        }
    }
}
