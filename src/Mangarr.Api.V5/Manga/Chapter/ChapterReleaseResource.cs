namespace Mangarr.Api.V5.Manga.Chapter;

// Sonarr divergence: NEW manga-domain wire-shape per Phase 16 STRUCT-08 — see DIVERGENCE.md.
// TV's Episode resource has no per-release nested collection because language lives on
// EpisodeFile.Languages, not on per-release rows. Manga's per-translation grain demands
// a nested collection — multilingual scanlations exist for manga but not for TV (Phase 16
// CONTEXT D-01 + D-02 + STRUCT-02). This resource is OUTPUT-only — NO PUT/POST endpoint
// accepts it as a body (mass-assignment threat T-16-05-01 mitigated structurally).
public class ChapterReleaseResource
{
    public int Id { get; set; }
    public string TranslatedLanguage { get; set; } = string.Empty;
    public string? ScanlationGroup { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string? ExternalId { get; set; }
}
