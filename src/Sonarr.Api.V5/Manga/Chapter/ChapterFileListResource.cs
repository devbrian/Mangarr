namespace Sonarr.Api.V5.Manga.Chapter;

// Sonarr divergence: NEW manga V5 list-resource per Phase 13 Plan 13-07.
// Role-match analog: src/Sonarr.Api.V5/EpisodeFiles/EpisodeFileListResource.cs.
// Body shape consumed by ChapterFileController bulk DELETE endpoint.
public class ChapterFileListResource
{
    public List<int> ChapterFileIds { get; set; } = [];
}
