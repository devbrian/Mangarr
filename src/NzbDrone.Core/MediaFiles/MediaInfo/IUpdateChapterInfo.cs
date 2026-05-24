namespace NzbDrone.Core.MediaFiles.MediaInfo
{
    using Manga = NzbDrone.Core.Manga.Manga;

    // Phase 30 Plan 30-05 (II2-03) — Manga peer of Sonarr's IUpdateMediaInfo (TV).
    //
    // D-05 LOCKED: probe-on-import ONLY — NO IHandle<MangaScannedEvent> backfill
    // daemon. The single Update(ChapterFile, Manga) entry point is invoked inline
    // by ImportApprovedChapters at step 3.5 (Pitfall 4 ordering preserved — AFTER
    // DB commit + filesystem move, BEFORE ChapterImportedEvent publish).
    //
    // Returns true if MediaInfo was populated AND the ChapterFile row was updated
    // (chapterFile.Id != 0 path); false if the file does not exist or the probe
    // returned null. D-09 non-fatal: implementations catch internal exceptions
    // and return false rather than propagating; the import pipeline wraps the
    // call in its own try/catch as additional defense.
    public interface IUpdateChapterInfo
    {
        bool Update(ChapterFile chapterFile, Manga manga);
    }
}
