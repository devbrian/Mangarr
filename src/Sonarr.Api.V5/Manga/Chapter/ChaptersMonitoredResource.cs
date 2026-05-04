namespace Sonarr.Api.V5.Manga.Chapter;

// Sonarr divergence: NEW manga V5 resource per Phase 7 D-07 — see DIVERGENCE.md.
// Role-match analog: src/Sonarr.Api.V5/Episodes/EpisodesMonitoredResource.cs (verbatim
// rename port — { EpisodeIds → ChapterIds }).
//
// Phase 8 cleanup: collapse with EpisodesMonitoredResource when Tv/ deletes.
public class ChaptersMonitoredResource
{
    public required List<int> ChapterIds { get; set; }
    public bool Monitored { get; set; }
}
