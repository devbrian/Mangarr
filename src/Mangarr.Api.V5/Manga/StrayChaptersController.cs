using Mangarr.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Manga;

namespace Mangarr.Api.V5.Manga;

// Sonarr divergence: NEW maintenance endpoint — companion to the ChapterSynthesisService
// density-floor stray-outlier guard. Lets the user inspect (GET, read-only dry-run) the
// synthesized phantom chapters a pre-fix reconciliation left ABOVE a manga's metadata
// chapter count, then prune them (POST). With-file strays are NEVER touched unless
// deleteFiles=true is passed explicitly, and even then go to the recycle bin (recoverable).
[V5ApiController("manga")]
public class StrayChaptersController : Controller
{
    private readonly IStrayChapterPruneService _pruneService;

    public StrayChaptersController(IStrayChapterPruneService pruneService)
    {
        _pruneService = pruneService;
    }

    // Dry-run: report the strays WITHOUT mutating anything. Safe to call repeatedly.
    [HttpGet("{id:int}/straychapters")]
    [Produces("application/json")]
    public Results<Ok<StrayChapterPruneResource>, NotFound> GetStrayChapters(int id)
    {
        var report = _pruneService.BuildReport(id);
        return report == null
            ? TypedResults.NotFound()
            : TypedResults.Ok(report.ToResource());
    }

    // Execute: delete the confident file-less junk; recycle-bin + delete the with-file strays ONLY
    // when deleteFiles=true; prune the UNCERTAIN file-less rows (no on-disk evidence anchors the
    // density cut) ONLY when pruneUncertain=true. Returns the same manifest shape with the deletion
    // counts populated.
    [HttpPost("{id:int}/straychapters")]
    [Produces("application/json")]
    public Results<Ok<StrayChapterPruneResource>, NotFound> PruneStrayChapters(
        int id,
        [FromQuery] bool deleteFiles = false,
        [FromQuery] bool pruneUncertain = false)
    {
        var report = _pruneService.Prune(id, deleteFiles, pruneUncertain);
        return report == null
            ? TypedResults.NotFound()
            : TypedResults.Ok(report.ToResource());
    }
}

public class StrayChapterPruneResource
{
    public int MangaId { get; set; }
    public string? MangaTitle { get; set; }
    public int? MetadataChapterCount { get; set; }
    public bool BaselineKnown { get; set; }
    public bool DryRun { get; set; }
    public decimal DensityCut { get; set; }
    public bool DiskEvidenceAboveBaseline { get; set; }
    public int FileLessStrayCount { get; set; }
    public int WithFileStrayCount { get; set; }
    public int UncertainStrayCount { get; set; }
    public int LegitExtensionCount { get; set; }
    public int DeletedChapterRowCount { get; set; }
    public int DeletedFileCount { get; set; }
    public List<StrayChapterResource> FileLessStrays { get; set; } = new();
    public List<StrayChapterResource> WithFileStrays { get; set; } = new();
    public List<StrayChapterResource> UncertainStrays { get; set; } = new();
    public List<StrayChapterResource> LegitExtension { get; set; } = new();
}

public class StrayChapterResource
{
    public int ChapterId { get; set; }
    public decimal ChapterNumber { get; set; }
    public bool Monitored { get; set; }
    public string? Title { get; set; }
    public DateTime? FirstReleaseDate { get; set; }
    public string? ExternalId { get; set; }
    public List<StrayFileResource> Files { get; set; } = new();
}

public class StrayFileResource
{
    public int ChapterFileId { get; set; }
    public string? Path { get; set; }
    public long Size { get; set; }
}

public static class StrayChapterPruneResourceMapper
{
    public static StrayChapterPruneResource ToResource(this StrayChapterPruneReport report) => new()
    {
        MangaId = report.MangaId,
        MangaTitle = report.MangaTitle,
        MetadataChapterCount = report.MetadataChapterCount,
        BaselineKnown = report.BaselineKnown,
        DryRun = report.DryRun,
        DensityCut = report.DensityCut,
        DiskEvidenceAboveBaseline = report.DiskEvidenceAboveBaseline,
        FileLessStrayCount = report.FileLessStrays.Count,
        WithFileStrayCount = report.WithFileStrays.Count,
        UncertainStrayCount = report.UncertainStrays.Count,
        LegitExtensionCount = report.LegitExtension.Count,
        DeletedChapterRowCount = report.DeletedChapterRowCount,
        DeletedFileCount = report.DeletedFileCount,
        FileLessStrays = report.FileLessStrays.Select(ToResource).ToList(),
        WithFileStrays = report.WithFileStrays.Select(ToResource).ToList(),
        UncertainStrays = report.UncertainStrays.Select(ToResource).ToList(),
        LegitExtension = report.LegitExtension.Select(ToResource).ToList(),
    };

    private static StrayChapterResource ToResource(StrayChapterInfo info) => new()
    {
        ChapterId = info.ChapterId,
        ChapterNumber = info.ChapterNumber,
        Monitored = info.Monitored,
        Title = info.Title,
        FirstReleaseDate = info.FirstReleaseDate,
        ExternalId = info.ExternalId,
        Files = info.Files.Select(f => new StrayFileResource
        {
            ChapterFileId = f.ChapterFileId,
            Path = f.Path,
            Size = f.Size,
        }).ToList(),
    };
}
