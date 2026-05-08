namespace Mangarr.Api.V5.Manga;

// Sonarr divergence: NEW manga V5 bulk-edit DTO per Phase 13 Plan 13-04 (sub-wave C
// F-CUTOFF-class silent-404 closure for /manga/editor). Mirrors the
// SeriesEditorResource shape (src/Mangarr.Api.V5/Series/SeriesEditorResource.cs)
// with manga-domain field divergences:
//
//   * Id-list field: rename to MangaIds (canonical aggregate is Manga).
//   * DROP the new-item-monitor enum (manga has no new-volume monitoring;
//     PROJECT.md Volumes/Seasons Out-of-Scope row).
//   * DROP the per-aggregate type enum (no manga-type enum yet).
//   * DROP the per-season-folder bool (no seasons per PROJECT.md Out-of-Scope).
//   * REPLACE the single quality-profile FK with TWO FK fields:
//     TranslationProfileId + CustomFormatProfileId (Phase 5 D-05 split).
//   * KEEP Monitored, RootFolderPath, Tags, ApplyTags, MoveFiles, DeleteFiles.
//   * KEEP AddImportListExclusion as a no-op forward-compat field (preserves
//     wire shape per useManga.ts:467 sending addImportListExclusion?: boolean;
//     Import Lists deferred to v1.1 per PROJECT.md so the controller MUST drop
//     this arg from the IMangaService.DeleteManga call — RESEARCH §Pitfall 4).
//
// Allow-list shape per T-13-03 mass-assignment mitigation: only the fields
// listed below survive deserialization; extra fields posted by an attacker are
// silently dropped (System.Text.Json default behavior).
//
// Mirrors useManga.ts:470-477 SaveMangaEditorPayload wire shape (camelCase
// after JSON serialization).
//
// Phase 15 collapse target: rename to BulkEditMangaResource or unify with TV
// SeriesEditorResource shape after Phase 8 domain rename completes.
public class MangaEditorResource
{
    public List<int> MangaIds { get; set; } = [];
    public bool? Monitored { get; set; }
    public int? TranslationProfileId { get; set; }
    public int? CustomFormatProfileId { get; set; }
    public string? RootFolderPath { get; set; }
    public List<int> Tags { get; set; } = [];
    public ApplyTags ApplyTags { get; set; }
    public bool MoveFiles { get; set; }
    public bool DeleteFiles { get; set; }

    // Forward-compat no-op field — controller does not pass this to
    // IMangaService.DeleteManga (RESEARCH §Pitfall 4 — manga's signature is
    // 2-arg only). Preserved to keep useManga.ts:467 wire shape stable.
    public bool AddImportListExclusion { get; set; }
}
