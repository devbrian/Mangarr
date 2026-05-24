using System;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles.MediaInfo;

namespace NzbDrone.Core.MediaFiles
{
    // Sonarr divergence: NEW manga sibling per Phase 6 PIPELINE-04 — see DIVERGENCE.md.
    // Role-match analog: src/NzbDrone.Core/MediaFiles/EpisodeFile.cs (TV).
    // Imported chapter artifact (CBZ / folder of images) on disk + provenance fields.
    // Phase 16.1 D-06: 2 group-axis fields (TranslatedLanguage + ScanlationGroup), not 3 —
    // ReleaseGroup absorbed into ScanlationGroup (scanlation groups ARE the release groups
    // for manga). See DIVERGENCE.md ScanlationGroup-as-canonical entry.
    // Phase 8 cleanup: collapse with EpisodeFile when Tv/ deletes.
    public class ChapterFile : ModelBase
    {
        public int MangaId { get; set; }
        public int ChapterId { get; set; }
        public string RelativePath { get; set; }
        public string Path { get; set; }
        public long Size { get; set; }
        public DateTime DateAdded { get; set; }
        public string OriginalFilePath { get; set; }
        public string TranslatedLanguage { get; set; }   // BCP-47 — provenance

        // Sonarr divergence: ScanlationGroup is the canonical "release group" axis for manga;
        // renamed from Sonarr's EpisodeFile.ReleaseGroup because scanlation groups ARE the
        // release groups for manga. (D-06; DIVERGENCE.md ScanlationGroup-as-canonical entry.)
        public string ScanlationGroup { get; set; }

        // Phase 30 Plan 30-05 (II2-03) — ImageSharp probe result. Nullable: existing rows
        // pre-Migration-004 stay NULL (D-05 probe-on-import only — no backfill daemon).
        // Round-tripped via EmbeddedDocumentConverter<ChapterMediaInfo> Dapper TypeHandler
        // (registered in TableMapping.cs); TEXT column added by Migration 004.
        public ChapterMediaInfo MediaInfo { get; set; }

        public override string ToString()
        {
            return string.Format("[{0}] {1}", Id, RelativePath);
        }

        public string GetSceneOrFileName()
        {
            if (RelativePath.IsNotNullOrWhiteSpace())
            {
                return System.IO.Path.GetFileNameWithoutExtension(RelativePath);
            }

            if (Path.IsNotNullOrWhiteSpace())
            {
                return System.IO.Path.GetFileNameWithoutExtension(Path);
            }

            return string.Empty;
        }
    }
}
