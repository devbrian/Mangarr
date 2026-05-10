using System;
using System.Collections.Generic;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Parser.Model;

// Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — Qualities/ DELETED per Plan 15-03;
// QualityModel field stripped from manga ManualImportFile.

namespace NzbDrone.Core.MediaFiles.MangaImport.Manual
{
    public class ManualImportFile : IEquatable<ManualImportFile>
    {
        public string Path { get; set; }
        public string FolderName { get; set; }
        public int MangaId { get; set; }
        public List<int> ChapterIds { get; set; }
        public int? ChapterFileId { get; set; }

        // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption — QualityModel stripped (Quality cascade per Plan 15-03).
        public List<Language> Languages { get; set; }
        public string ScanlationGroup { get; set; }    // Phase 16.1 D-06 — canonical "release group" axis for manga
        public int IndexerFlags { get; set; }
        public ReleaseType ReleaseType { get; set; }
        public string DownloadId { get; set; }

        public bool Equals(ManualImportFile other)
        {
            if (other == null)
            {
                return false;
            }

            return Path.PathEquals(other.Path);
        }

        public override bool Equals(object obj)
        {
            if (obj == null)
            {
                return false;
            }

            if (obj.GetType() != GetType())
            {
                return false;
            }

            return Path.PathEquals(((ManualImportFile)obj).Path);
        }

        public override int GetHashCode()
        {
            return Path != null ? Path.GetHashCode() : 0;
        }
    }
}
