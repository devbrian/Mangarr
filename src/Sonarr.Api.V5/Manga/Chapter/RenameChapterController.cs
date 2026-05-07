using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MediaFiles;
using Sonarr.Http;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Manga.Chapter;

// Sonarr divergence: NEW manga V5 controller per Phase 13 Plan 13-06 — see DIVERGENCE.md.
// Role-match analog: src/Sonarr.Api.V5/Episodes/RenameEpisodeController.cs (lines 10-48).
//
// Manga sibling preserves:
//   * Bare `Controller` base (NOT RestControllerWithSignalR — rename preview is a
//     read-only on-demand endpoint, no SignalR push contract).
//   * `[HttpGet]` + `[HttpGet("bulk")]` action templates.
//   * IRenameChapterFileService 3-overload pattern (single-id / single-id+number /
//     bulk-ids) — manga peer of TV's IRenameEpisodeFileService.
//   * BadRequestException-driven input validation on bulk endpoint (manga peer of
//     TV's seriesIds positive-int + non-empty checks).
//
// Manga sibling diverges from RenameEpisodeController:
//   * `[V5ApiController("manga/rename")]` (NOT bare `"rename"` like the TV peer)
//     so the route path is `/api/v5/manga/rename` — keeps manga endpoints under the
//     `/api/v5/manga/...` namespace per Phase 13 D-13-07 Series-rename-family rule.
//   * `seriesId` → `mangaId`, `seasonNumber: int?` → `chapterNumber: decimal?`
//     (Phase 2 D-12 — chapter numbers are decimal not int).
//   * `IRenameEpisodeFileService` → `IRenameChapterFileService`.
//   * `RenameEpisodeResource` → `RenameChapterResource`.
//
// Phase 15 cleanup: TV peer deletion + namespace rename collapses this to
// RenameController.

[V5ApiController("manga/rename")]
public class RenameChapterController : Controller
{
    private readonly IRenameChapterFileService _renameChapterFileService;

    public RenameChapterController(IRenameChapterFileService renameChapterFileService)
    {
        _renameChapterFileService = renameChapterFileService;
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<List<RenameChapterResource>> GetChapters(int mangaId, decimal? chapterNumber)
    {
        if (chapterNumber.HasValue)
        {
            return TypedResults.Ok(_renameChapterFileService.GetRenamePreviews(mangaId, chapterNumber).ToResource());
        }

        return TypedResults.Ok(_renameChapterFileService.GetRenamePreviews(mangaId).ToResource());
    }

    [HttpGet("bulk")]
    [Produces("application/json")]
    public Results<Ok<List<RenameChapterResource>>, BadRequest> GetChapters([FromQuery] List<int> mangaIds)
    {
        if (mangaIds is { Count: 0 })
        {
            throw new BadRequestException("mangaIds must be provided");
        }

        if (mangaIds.Any(mangaId => mangaId <= 0))
        {
            throw new BadRequestException("mangaIds must be positive integers");
        }

        return TypedResults.Ok(_renameChapterFileService.GetRenamePreviews(mangaIds).ToResource());
    }
}
