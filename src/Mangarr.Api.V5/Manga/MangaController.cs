using System.Data.SQLite;
using FluentValidation;
using Mangarr.Http;
using Mangarr.Http.REST;
using Mangarr.Http.REST.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NLog;
using Npgsql;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Manga.Commands;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.MangaStats;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.SignalR;

namespace Mangarr.Api.V5.Manga;

// Phase 2 developer-surface CRUD controller per Plan 02-10. Mirrors Mangarr's
// SeriesController shape (Mangarr.Api.V5/Series/SeriesController.cs) but slimmed for
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
    private readonly IMangaStatisticsService _mangaStatisticsService;
    private readonly IMapMangaCoversToLocal _coverMapper;
    private readonly IManageCommandQueue _commandQueueManager;
    private readonly Logger _logger;

    public MangaController(IBroadcastSignalRMessage signalRBroadcaster,
                           IMangaService mangaService,
                           IAddMangaService addMangaService,
                           IMangaStatisticsService mangaStatisticsService,
                           IMapMangaCoversToLocal coverMapper,
                           IManageCommandQueue commandQueueManager,
                           Logger logger)
        : base(signalRBroadcaster)
    {
        _mangaService = mangaService;
        _addMangaService = addMangaService;
        _mangaStatisticsService = mangaStatisticsService;
        _coverMapper = coverMapper;
        _commandQueueManager = commandQueueManager;
        _logger = logger;

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

        // Sonarr-canonical SeriesController.AllSeries pattern (issue #335): ONE SQL-aggregated
        // statistics query for the whole library, keyed by MangaId — never an inline per-manga
        // chapter-list read. The repository GROUP BY only returns rows for manga that have
        // chapters, so the dictionary miss for a zero-chapter manga falls back to a zeroed
        // resource (F-05 always-present contract) inside LinkMangaStatistics.
        var statsByMangaId = _mangaStatisticsService.MangaStatistics().ToDictionary(s => s.MangaId);
        var result = new List<MangaResource>(all.Count);

        foreach (var m in all)
        {
            var resource = MapResource(m);

            if (resource != null)
            {
                LinkMangaStatistics(resource, statsByMangaId.GetValueOrDefault(m.Id));
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

        var resource = MapResource(manga);

        if (resource != null)
        {
            FetchAndLinkMangaStatistics(resource);
        }

        return resource;
    }

    [RestPostById]
    [Consumes("application/json")]
    [Produces("application/json")]
    public Results<Created<MangaResource>, NotFound, Conflict<string>> AddManga([FromBody] MangaResource resource)
    {
        try
        {
            var manga = resource.ToModel();

            // Bug fix new-manga-default-monitored (2026-05-08): default Monitored=true
            // for Add Manga unless the user explicitly chose Monitor=None on the dialog.
            // Without this default, newly-added manga always land with Monitored=false
            // because (a) the frontend AddMangaPayload omits the `monitored` key
            // (5-value MangaMonitor on the form drives chapter-level monitoring, not
            // manga-level Monitored) and (b) MangaResource.Monitored therefore receives
            // the C# bool default (false) on JSON deserialization. Mirrors the SHAPE
            // of Sonarr's TV-side Add flow where Series.Monitored arrives true on
            // every Add (the inverted symmetric of AddMangaService.PrepareForAdd's
            // gap-05 block at AddMangaService.cs:298-301 which only forces FALSE on
            // Monitor=None — that block stays as the explicit-opt-out gate; this
            // controller default is the implicit-opt-in default that pairs with it).
            if (manga != null)
            {
                manga.Monitored = manga.AddOptions?.Monitor != MangaMonitor.None;
            }

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
        catch (SQLiteException ex) when (ex.ResultCode == SQLiteErrorCode.Constraint &&
                                         IsMangaExternalIdUniqueViolation(ex.Message))
        {
            // Issue #213 fix: when two concurrent POSTs both pass the in-memory
            // FindByMangaDexId / FindByMalId / FindByAniListId checks in
            // AddMangaService.PrepareForAdd before either insert commits, the
            // UNIQUE indexes on Manga.MangaDexId / MalId / AniListId (Migration 001
            // IX_Manga_*) surface the second-comer as a SQLITE_CONSTRAINT violation
            // on Insert. Map to 409 Conflict — first-wins semantic, matching the
            // existing happy-path InvalidOperationException branch above.
            //
            // The `when` predicate gates this catch to UNIQUE violations on the
            // external-ID columns specifically; any other constraint violation
            // (NOT NULL, CHECK, FK) propagates unchanged so the caller sees the
            // true 500 root cause rather than a misleading 409. CodeRabbit
            // review on PR #214 surfaced the over-broad-catch concern.
            _logger.Debug(ex, "Add Manga rejected by UNIQUE constraint (concurrent-POST race; issue #213)");
            return TypedResults.Conflict("Manga with this external ID already exists");
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation &&
                                           IsMangaExternalIdConstraint(ex.ConstraintName))
        {
            // PostgreSQL peer of the SQLite block above. The unit_test_postgres
            // CI job exercises this path; the FluentMigrator-emitted UNIQUE
            // indexes carry the same names across both dialects (IX_Manga_*),
            // so ConstraintName matching gives us the same precision the SQLite
            // column-name match provides.
            _logger.Debug(ex, "Add Manga rejected by UNIQUE constraint (concurrent-POST race; issue #213)");
            return TypedResults.Conflict("Manga with this external ID already exists");
        }
    }

    // Issue #213 — gate the SQLite Constraint catch to UNIQUE violations on the
    // three external-ID columns only. SQLite's UNIQUE-violation message form is
    // `UNIQUE constraint failed: Manga.<Column>` — match on the qualified column
    // name so the gate doesn't false-positive on a violation against an unrelated
    // index/table (or a future NOT NULL / CHECK constraint on the Manga table).
    private static bool IsMangaExternalIdUniqueViolation(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        return message.Contains("Manga.MangaDexId", StringComparison.Ordinal) ||
               message.Contains("Manga.MalId", StringComparison.Ordinal) ||
               message.Contains("Manga.AniListId", StringComparison.Ordinal);
    }

    // Postgres peer — match against the index name carried verbatim on
    // PostgresException.ConstraintName by Npgsql.
    private static bool IsMangaExternalIdConstraint(string? constraintName)
    {
        return constraintName is "IX_Manga_MangaDexId"
                              or "IX_Manga_MalId"
                              or "IX_Manga_AniListId";
    }

    [RestPutById]
    [Consumes("application/json")]
    [Produces("application/json")]
    public Results<Accepted<MangaResource>, NotFound> UpdateManga([FromBody] MangaResource resource, [FromQuery] bool moveFiles = false)
    {
        // BL-01 fix: GetManga now returns null on missing (was throwing
        // ModelNotFoundException upstream). Surface as 404 instead of NRE / 500.
        var existing = _mangaService.GetManga(resource.Id);
        if (existing == null)
        {
            return TypedResults.NotFound();
        }

        // issue-81 wire-up: enqueue MoveMangaCommand BEFORE ApplyChanges so the
        // command sees the original on-disk path. Mirrors upstream
        // SeriesController.UpdateSeries (Sonarr/v5-develop:src/Sonarr.Api.V5/Series/
        // SeriesController.cs:198-212). MoveMangaService runs async on the command
        // queue (SendUpdatesToClient=true, RequiresDiskAccess=true → visible in
        // System → Tasks); idempotency short-circuit at MoveMangaService.cs:68-72
        // logs "is already in the specified location" when source == destination.
        // The backend MoveMangaCommand + BulkMoveMangaCommand + MoveMangaService
        // were shipped Phase 2 Plan 02-16 but never had a publish site — this is
        // the single-edit wire-up; MangaEditorController owns the bulk wire-up.
        if (moveFiles)
        {
            var sourcePath = existing.Path;
            var destinationPath = resource.Path;

            _commandQueueManager.Push(new MoveMangaCommand
            {
                MangaId = existing.Id,
                SourcePath = sourcePath,
                DestinationPath = destinationPath
            },
                trigger: CommandTrigger.Manual);
        }

        // issue #96 fix (2026-05-12): save existing.Path BEFORE ApplyChanges so we
        // can restore it if the inbound resource omits `path`. Manga.ApplyChanges
        // (src/NzbDrone.Core/Manga/Manga.cs:135) unconditionally copies
        // `Path = other.Path` — added by issue #81 so MoveMangaCommand can land the
        // new on-disk path. But external API callers (curl, scripts, third-party
        // integrations) frequently PUT partial bodies that omit `path`. Without the
        // save/restore below, ApplyChanges clobbers existing.Path to null/empty in
        // memory, then `_mangaService.UpdateManga(existing, …)` trips the SQLite
        // NOT NULL constraint on Manga.Path (001_mangarr_baseline.cs:510) and
        // surfaces as a generic HTTP 500. Mirrors the PR #95 save/restore pattern
        // in RefreshMangaService.RefreshMangaInfo (src/NzbDrone.Core/Manga/
        // RefreshMangaService.cs:224 + 235) which solved the same foot-gun on the
        // metadata-refresh path. Frontend MangaResourceMapper.ToModel always sends
        // Path so the UI is unaffected; this fix preserves the moveFiles wire-up
        // above (the moveFiles branch reads resource.Path BEFORE this restore) and
        // is a no-op when the caller supplies a non-empty Path.
        var userPath = existing.Path;

        existing.ApplyChanges(resource.ToModel()!);

        existing.Path = !string.IsNullOrWhiteSpace(existing.Path) ? existing.Path : userPath;

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
    public NoContent DeleteManga(int id, bool deleteFiles = false, bool addImportListExclusion = true)
    {
        // GH #241 follow-up: the single-manga DELETE endpoint previously called the
        // 2-arg IMangaService.DeleteManga(List<int>, bool) overload, which unconditionally
        // defaults addImportListExclusion to true in the underlying 3-arg delegate (see
        // MangaService.cs ~line 162). That dropped the FE's `addImportListExclusion=false`
        // query param silently, so unchecking the Delete modal checkbox still added an
        // auto-exclusion row. Calling the 3-arg overload directly threads the user's
        // choice through to MangaDeletedEvent → ImportListExclusionService.Handle.
        //
        // Default stays `true` to match Sonarr UX (re-add prevention is the safer default
        // for users who don't toggle the checkbox).
        _mangaService.DeleteManga(new List<int> { id }, deleteFiles, addImportListExclusion);

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

    // Issue #335: statistics now come from the SQL-aggregated IMangaStatisticsService
    // (the SeriesStatisticsService analog under NzbDrone.Core/MangaStats/) instead of an
    // inline per-manga chapter-list read. The list path (GetAll) batches a single
    // MangaStatistics() query and links via the dictionary overload below; the single-GET
    // path uses this per-id query. Mirrors Sonarr SeriesController.FetchAndLinkSeriesStatistics.
    private void FetchAndLinkMangaStatistics(MangaResource resource)
    {
        LinkMangaStatistics(resource, _mangaStatisticsService.MangaStatistics(resource.Id));
    }

    // Always attach a Statistics object (zeroed when stats are null/absent) so the Phase 7
    // MangaIndex tiles — which read episodeCount/episodeFileCount eagerly — never fall back to
    // a missing-field "0 / 0" for a manga that actually has downloaded chapters (the original
    // F-05 bug). The mapper handles the null → zeroed projection.
    private static void LinkMangaStatistics(MangaResource resource, MangaStatistics? statistics)
    {
        resource.Statistics = statistics.ToResource();
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
        // WR-04 fix: catch NotFoundException so a benign delete-race between
        // event publish and SignalR fan-out does not surface as an ERROR-level
        // log line. BroadcastResourceChange(action, int) round-trips through
        // the controller-overridden GetResourceById which throws on missing.
        try
        {
            BroadcastResourceChange(ModelAction.Updated, message.Manga.Id);
        }
        catch (NotFoundException)
        {
            _logger.Debug("MangaEditedEvent: manga {Id} no longer exists, skipping SignalR fan-out (benign delete-race)", message.Manga.Id);
        }
    }

    // Phase 10 Plan 10-05 (sub-wave A FINDINGS gap_in_scope close-out): SignalR consumer for
    // MangaRenamedEvent published by RenameChapterFileService at src/NzbDrone.Core/MediaFiles/RenameChapterFileService.cs
    // (Plan 02-10 ordering invariant — DB write FIRST, event LAST). Mirrors SeriesController.cs
    // Handle(SeriesRenamedEvent) shape verbatim — UI re-renders manga details + chapter list after
    // file rename completes.
    [NonAction]
    public void Handle(MangaRenamedEvent message)
    {
        // WR-04 fix: catch NotFoundException so a benign delete-race between
        // event publish and SignalR fan-out does not surface as an ERROR-level
        // log line. BroadcastResourceChange(action, int) round-trips through
        // the controller-overridden GetResourceById which throws on missing.
        try
        {
            BroadcastResourceChange(ModelAction.Updated, message.Manga.Id);
        }
        catch (NotFoundException)
        {
            _logger.Debug("MangaRenamedEvent: manga {Id} no longer exists, skipping SignalR fan-out (benign delete-race)", message.Manga.Id);
        }
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
            // WR-01 fix: catch NotFoundException INSIDE the foreach so iteration
            // continues to surviving items if any one row is concurrently deleted
            // between bulk-edit publish and SignalR fan-out (benign race —
            // SeriesController's analogous handler short-circuits via in-memory
            // payload null-check; Mangarr's pragmatic equivalent is this catch).
            try
            {
                BroadcastResourceChange(ModelAction.Updated, manga.Id);
            }
            catch (NotFoundException)
            {
                _logger.Debug("MangaBulkEditedEvent: manga {Id} no longer exists, skipping SignalR fan-out for this row (benign delete-race)", manga.Id);
            }
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
        // WR-04 fix: catch NotFoundException so a benign delete-race between
        // event publish and SignalR fan-out does not surface as an ERROR-level
        // log line. BroadcastResourceChange(action, int) round-trips through
        // the controller-overridden GetResourceById which throws on missing.
        try
        {
            BroadcastResourceChange(ModelAction.Updated, message.ChapterFile.MangaId);
        }
        catch (NotFoundException)
        {
            _logger.Debug("ChapterFileAddedEvent: manga {Id} no longer exists, skipping SignalR fan-out (benign delete-race)", message.ChapterFile.MangaId);
        }
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

        // WR-04 fix: catch NotFoundException so a benign delete-race between
        // event publish and SignalR fan-out does not surface as an ERROR-level
        // log line. BroadcastResourceChange(action, int) round-trips through
        // the controller-overridden GetResourceById which throws on missing.
        // Especially plausible here: housekeeping-driven full-library delete
        // can race a tracked-download finalize-and-delete on the same manga.
        try
        {
            BroadcastResourceChange(ModelAction.Updated, message.ChapterFile.MangaId);
        }
        catch (NotFoundException)
        {
            _logger.Debug("ChapterFileDeletedEvent: manga {Id} no longer exists, skipping SignalR fan-out (benign delete-race)", message.ChapterFile.MangaId);
        }
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
            // WR-01 fix: catch NotFoundException INSIDE the foreach so iteration
            // continues for surviving ids if any one row is concurrently deleted
            // between bulk-import publish and SignalR fan-out. MangaImportedEvent
            // carries only `List<int> MangaIds` (no Manga objects), so the
            // in-memory short-circuit available to MangaBulkEditedEvent isn't an
            // option here — catching post-fetch is the only path.
            try
            {
                BroadcastResourceChange(ModelAction.Updated, mangaId);
            }
            catch (NotFoundException)
            {
                _logger.Debug("MangaImportedEvent: manga {Id} no longer exists, skipping SignalR fan-out for this row (benign delete-race)", mangaId);
            }
        }
    }
}
