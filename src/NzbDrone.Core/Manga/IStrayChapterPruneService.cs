using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Manga
{
    public interface IStrayChapterPruneService
    {
        // Dry-run analysis ONLY — never mutates. Returns null when the manga id is unknown.
        StrayChapterPruneReport BuildReport(int mangaId);

        // Executes the prune. File-less strays are always deleted; with-file strays are
        // recycle-binned + deleted ONLY when deleteFiles is true. Returns null when unknown.
        StrayChapterPruneReport Prune(int mangaId, bool deleteFiles);
    }

    public sealed class StrayChapterPruneReport
    {
        public int MangaId { get; set; }
        public string MangaTitle { get; set; }

        // The trusted metadata chapter count (Manga.TotalChapterCount) — the baseline above
        // which a row is considered a synthesized stray candidate.
        public int? MetadataChapterCount { get; set; }

        // false => no metadata count, so strays cannot be distinguished (nothing is pruned).
        public bool BaselineKnown { get; set; }

        public bool DryRun { get; set; }

        // Strays with no artifact on disk — safe to delete.
        public List<StrayChapterInfo> FileLessStrays { get; set; } = new();

        // Strays that DO carry a ChapterFile — a mislabeled release may be real content, so these
        // are reported for review and only deleted (recycle bin) on an explicit deleteFiles opt-in.
        public List<StrayChapterInfo> WithFileStrays { get; set; } = new();

        // Populated by Prune (0 on a dry-run).
        public int DeletedChapterRowCount { get; set; }
        public int DeletedFileCount { get; set; }
    }

    public sealed class StrayChapterInfo
    {
        public int ChapterId { get; set; }
        public decimal ChapterNumber { get; set; }
        public bool Monitored { get; set; }
        public string Title { get; set; }
        public DateTime? FirstReleaseDate { get; set; }
        public string ExternalId { get; set; }
        public List<StrayFileInfo> Files { get; set; } = new();
    }

    public sealed class StrayFileInfo
    {
        public int ChapterFileId { get; set; }
        public string Path { get; set; }
        public long Size { get; set; }
    }
}
