using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Manga
{
    public interface IStrayChapterPruneService
    {
        // Dry-run analysis ONLY — never mutates. Returns null when the manga id is unknown.
        StrayChapterPruneReport BuildReport(int mangaId);

        // Executes the prune. Confident file-less junk (above the density cut, anchored by
        // on-disk evidence) is always deleted; with-file strays are recycle-binned + deleted
        // ONLY when deleteFiles is true; UNCERTAIN file-less rows (no on-disk evidence anchors
        // the cut, so they could be legitimately-wanted chapters from a stale metadata count)
        // are left alone unless pruneUncertain is true. Returns null when the id is unknown.
        StrayChapterPruneReport Prune(int mangaId, bool deleteFiles, bool pruneUncertain = false);
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

        // The density cut anchored on WITH-FILE evidence above the baseline (ChapterDensityCut).
        // Rows at/below this are a dense real extension past a stale metadata count (spared);
        // rows ABOVE it are the sparse outliers (junk candidates). Equals the baseline when no
        // on-disk evidence anchors a higher cut.
        public decimal DensityCut { get; set; }

        // Whether any on-disk (with-file) chapter exists above the baseline to anchor the cut.
        // When false the cut falls back to the baseline and the file-less outliers are only
        // UNCERTAIN (could be legitimately-wanted, ungrabbed chapters) — not confident junk.
        public bool DiskEvidenceAboveBaseline { get; set; }

        // Confident file-less junk: above the cut AND anchored by on-disk evidence. Safe to
        // delete (nothing on disk to lose; a genuinely-real number re-synthesizes via the fixed
        // density cut on next search). Deleted by Prune unconditionally.
        public List<StrayChapterInfo> FileLessStrays { get; set; } = new();

        // Outliers that DO carry a ChapterFile (above the cut) — a mislabeled release may be real
        // content, so reported for review and only deleted (recycle bin) on an explicit
        // deleteFiles opt-in.
        public List<StrayChapterInfo> WithFileStrays { get; set; } = new();

        // File-less outliers above the baseline with NO on-disk evidence to anchor the cut.
        // Could be legitimately-wanted chapters from a stale metadata count OR phantoms — needs a
        // gateway search to decide, so NEVER auto-deleted (only on an explicit pruneUncertain opt-in).
        public List<StrayChapterInfo> UncertainStrays { get; set; } = new();

        // Rows at/below the cut: a dense real extension past a stale metadata count. Reported for
        // visibility but NEVER pruned (pruning them would delete real content).
        public List<StrayChapterInfo> LegitExtension { get; set; } = new();

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
