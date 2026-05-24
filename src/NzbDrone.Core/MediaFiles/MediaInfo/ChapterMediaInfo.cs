using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.MediaFiles.MediaInfo
{
    // Phase 30 Plan 30-05 (II2-03) — Manga peer of Sonarr's MediaInfoModel (TV).
    // Stored as a JSON-serialized blob in ChapterFiles.MediaInfo (TEXT column added by
    // Migration 004); round-tripped via the global EmbeddedDocumentConverter (registered
    // generically in TableMapping.RegisterEmbeddedConverter — the IEmbeddedDocument
    // marker IS the registration contract; no per-type explicit registration needed).
    //
    // All probe fields are nullable per D-09 null-skip render — MangaFileNameBuilder
    // tokens render empty when the relevant subfield is null (no "0 pages" / "Unknown"
    // defaults). Pre-Migration-004 rows stay MediaInfo = NULL entirely (D-05 no daemon).
    //
    // SchemaRevision mirrors Sonarr's MediaInfoModel.SchemaRevision precedent — a
    // non-nullable forward-migration marker so future v1.3+ probe-shape changes can
    // identify rows written under Phase 30's shape (probe samples first + middle + last
    // page only per D-07; future revisions may sample more densely or store typed columns).
    public class ChapterMediaInfo : IEmbeddedDocument
    {
        public int? PageCount { get; set; }

        public bool? Color { get; set; }

        public int? DpiHorizontal { get; set; }

        public int SchemaRevision { get; set; } = 1;
    }
}
