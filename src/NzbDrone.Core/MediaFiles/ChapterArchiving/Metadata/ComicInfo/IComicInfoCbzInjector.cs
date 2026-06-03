using NzbDrone.Core.Manga;

namespace NzbDrone.Core.MediaFiles.ChapterArchiving.Metadata.ComicInfo
{
    /// <summary>
    /// Phase 38 CINFO-01 — injects a single <c>ComicInfo.xml</c> entry into a finished CBZ.
    ///
    /// The gateway download client (Phase 38) delivers page-images-only CBZs — ComicInfo is no
    /// longer written during in-process archive construction. This injector becomes the SOLE
    /// writer of ComicInfo.xml for the import pipeline (Phase 39 retires
    /// <c>CbzChapterArchiver</c>'s metadata-writer path). It opens the already-finished archive
    /// with <see cref="System.IO.Compression.ZipArchiveMode.Update"/> and upserts ONE entry,
    /// reusing <see cref="ComicInfoXmlBuilder"/> + <see cref="AgeRatingMapper"/> verbatim (both
    /// MUST survive Phase 39).
    ///
    /// Always-upsert (D-A): runs on EVERY CBZ import unconditionally — no gateway-only gate, no
    /// download-client-type discriminator, no ComicInfo-presence check.
    ///
    /// Failure contract (D-B / D-B1): the implementation runs a Warn → retry-once-after-short-delay
    /// → fatal ladder internally. A persisted (second) failure RE-THROWS so the caller's existing
    /// per-decision try/catch publishes <c>ChapterImportFailedEvent</c> (the injector itself does
    /// NOT publish any event — no duplicate publish).
    /// </summary>
    public interface IComicInfoCbzInjector
    {
        /// <summary>
        /// Upsert a single <c>ComicInfo.xml</c> entry into the finished CBZ at
        /// <see cref="ChapterFile.Path"/>, building the document from Mangarr's persisted metadata
        /// (<paramref name="manga"/>/<paramref name="chapter"/> + the persisted
        /// <see cref="ChapterFile.ScanlationGroup"/> / <see cref="ChapterFile.TranslatedLanguage"/>)
        /// with a <c>&lt;PageCount&gt;</c> derived from the real image-entry count.
        /// </summary>
        /// <param name="chapterFile">The persisted ChapterFile — carries <c>.Path</c> + the
        /// resolved group/language provenance (D-C2).</param>
        /// <param name="manga">The resolved manga aggregate (<c>lc.Manga</c>).</param>
        /// <param name="chapter">The resolved chapter aggregate (<c>lc.Chapter</c>).</param>
        void Inject(ChapterFile chapterFile, NzbDrone.Core.Manga.Manga manga, Chapter chapter);
    }
}
