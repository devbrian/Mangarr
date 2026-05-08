using Mangarr.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles;

namespace Mangarr.Api.V5.FileSystem;

[V5ApiController]
public class FileSystemController : Controller
{
    private readonly IFileSystemLookupService _fileSystemLookupService;
    private readonly IDiskProvider _diskProvider;
    private readonly IMangaDiskScanService _diskScanService;

    public FileSystemController(IFileSystemLookupService fileSystemLookupService,
                            IDiskProvider diskProvider,
                            IMangaDiskScanService diskScanService)
    {
        _fileSystemLookupService = fileSystemLookupService;
        _diskProvider = diskProvider;
        _diskScanService = diskScanService;
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<FileSystemResult> GetContents(string? path, bool includeFiles = false, bool allowFoldersWithoutTrailingSlashes = false)
    {
        return TypedResults.Ok(_fileSystemLookupService.LookupContents(path, includeFiles, allowFoldersWithoutTrailingSlashes));
    }

    [HttpGet("type")]
    [Produces("application/json")]
    public Ok<object> GetEntityType(string path)
    {
        if (_diskProvider.FileExists(path))
        {
            return TypedResults.Ok((object)new { type = "file" });
        }

        // Return folder even if it doesn't exist on disk to avoid leaking anything from the UI about the underlying system
        return TypedResults.Ok((object)new { type = "folder" });
    }

    [HttpGet("mediafiles")]
    [Produces("application/json")]
    public Ok<IEnumerable<object>> GetMediaFiles(string path)
    {
        if (!_diskProvider.FolderExists(path))
        {
            return TypedResults.Ok(Enumerable.Empty<object>());
        }

        // Sonarr divergence: Phase 15 Plan 15-10 — IDiskScanService.GetVideoFiles stripped (TV-only).
        // Manga uses MangaDiskScanService.GetMediaFiles in the import pipeline; this V5 endpoint
        // is V1-deferred for manga (no per-file enumeration UI in v1). Returns empty list.
        return TypedResults.Ok(Enumerable.Empty<object>());
    }
}
