using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Messaging.Commands;
using Mangarr.Http;

namespace Mangarr.Api.V5.Manga;

// Sonarr divergence: NEW manga V5 bulk-action controller per Phase 13 Plan 13-04
// (sub-wave C — F-CUTOFF-class silent-404 closure for /manga/editor PUT + DELETE).
//
// Role-match analog: src/Mangarr.Api.V5/Series/SeriesEditorController.cs:13-115 —
// same role (bulk-action editor for the canonical aggregate) + same data flow
// (PUT applies field deltas across an id-list, DELETE removes by id-list).
//
// Divergences from the TV analog:
//   * Route literal: [V5ApiController("manga/editor")] (NOT "series/editor").
//   * Inject IMangaService (manga peer of ISeriesService).
//   * SaveAll PUT field deltas: drops MonitorNewItems / SeriesType / SeasonFolder
//     (manga-out-of-scope per PROJECT.md Volumes/Seasons row); replaces
//     QualityProfileId with TWO fields — TranslationProfileId + CustomFormatProfileId
//     (Phase 5 D-05 split).
//   * No BulkMoveMangaCommand publish branch — manga has no BulkMoveSeriesCommand
//     peer in v1 (verified via Grep). The MoveFiles bool still gates the
//     useExistingRelativeFolder argument passed to IMangaService.UpdateManga so
//     the file-move semantics propagate via the standard UpdateManga path.
//   * DeleteManga DELETE call: 2-arg signature per RESEARCH §Pitfall 4 (manga's
//     IMangaService.DeleteManga is `(List<int>, bool deleteFiles)` — NOT 3-arg
//     like TV; Import Lists deferred to v1.1 per PROJECT.md). The
//     MangaEditorResource.AddImportListExclusion field is preserved for forward-
//     compat wire-shape stability but the controller drops it from the call.
//   * Validator: empty MangaEditorValidator (see MangaEditorValidator.cs comment)
//     — preserves the SeriesEditorController dependency-injection shape for
//     easier Phase 15 collapse.
//
// Anti-pattern alert (RESEARCH §Pitfall 1): this controller MUST extend bare
// Controller — NOT RestController<T> or RestControllerWithSignalR<,>. Bulk-
// action controllers do not own a single resource id and must NOT broadcast
// SignalR from the editor site (MangaController.IHandle<MangaUpdatedEvent> /
// IHandle<MangaBulkEditedEvent> already broadcast post-update — broadcasting
// from the editor would double-fire).
//
// Closes silent 404 from useManga.ts:608 PUT (useSaveMangaEditor) and
// useManga.ts:655 DELETE (useBulkDeleteManga). Phase 15 collapse target:
// rename to BulkEditMangaController or merge with SeriesEditorController shape
// after Phase 8 Series→Manga rename completes.
[V5ApiController("manga/editor")]
public class MangaEditorController : Controller
{
    private readonly IMangaService _mangaService;
    private readonly IManageCommandQueue _commandQueueManager;
    private readonly MangaEditorValidator _mangaEditorValidator;

    public MangaEditorController(IMangaService mangaService,
                                 IManageCommandQueue commandQueueManager,
                                 MangaEditorValidator mangaEditorValidator)
    {
        _mangaService = mangaService;
        _commandQueueManager = commandQueueManager;
        _mangaEditorValidator = mangaEditorValidator;
    }

    [HttpPut]
    public Results<Ok<List<MangaResource>>, BadRequest> SaveAll([FromBody] MangaEditorResource resource)
    {
        var mangaToUpdate = _mangaService.GetManga(resource.MangaIds);

        foreach (var manga in mangaToUpdate)
        {
            if (resource.Monitored.HasValue)
            {
                manga.Monitored = resource.Monitored.Value;
            }

            if (resource.TranslationProfileId.HasValue)
            {
                manga.TranslationProfileId = resource.TranslationProfileId.Value;
            }

            if (resource.CustomFormatProfileId.HasValue)
            {
                manga.CustomFormatProfileId = resource.CustomFormatProfileId.Value;
            }

            if (resource.RootFolderPath.IsNotNullOrWhiteSpace())
            {
                manga.RootFolderPath = resource.RootFolderPath;
            }

            if (resource.Tags != null)
            {
                var newTags = resource.Tags;
                var applyTags = resource.ApplyTags;

                switch (applyTags)
                {
                    case ApplyTags.Add:
                        newTags.ForEach(t => manga.Tags.Add(t));
                        break;
                    case ApplyTags.Remove:
                        newTags.ForEach(t => manga.Tags.Remove(t));
                        break;
                    case ApplyTags.Replace:
                        manga.Tags = new HashSet<int>(newTags);
                        break;
                }
            }

            var validationResult = _mangaEditorValidator.Validate(manga);

            if (!validationResult.IsValid)
            {
                throw new ValidationException(validationResult.Errors);
            }
        }

        var updated = _mangaService.UpdateManga(mangaToUpdate, !resource.MoveFiles);

        var result = new List<MangaResource>(updated.Count);
        foreach (var m in updated)
        {
            var mapped = m.ToResource();
            if (mapped != null)
            {
                result.Add(mapped);
            }
        }

        return TypedResults.Ok(result);
    }

    [HttpDelete]
    public NoContent DeleteManga([FromBody] MangaEditorResource resource)
    {
        // RESEARCH §Pitfall 4: 2-arg call only — manga's IMangaService.DeleteManga
        // signature is (List<int> mangaIds, bool deleteFiles). The
        // resource.AddImportListExclusion field is intentionally NOT passed
        // (Import Lists deferred to v1.1 per PROJECT.md; field preserved on the
        // resource for forward-compat wire shape).
        _mangaService.DeleteManga(resource.MangaIds, resource.DeleteFiles);

        return TypedResults.NoContent();
    }
}
