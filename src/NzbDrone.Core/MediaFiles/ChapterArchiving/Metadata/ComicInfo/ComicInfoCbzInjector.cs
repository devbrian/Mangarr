using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.ChapterArchiving.Metadata.ComicInfo
{
    /// <summary>
    /// Phase 38 CINFO-01 — net-new injector that upserts a single <c>ComicInfo.xml</c> entry into
    /// a finished CBZ.
    ///
    /// KEY DIVERGENCE from <c>CbzChapterArchiver</c> (PATTERNS §"ZipArchive write idiom"): the
    /// archiver opens a fresh FileStream in create mode and streams every page. This injector opens
    /// the ALREADY-FINISHED archive with <see cref="ZipArchiveMode.Update"/> and upserts ONLY the
    /// ComicInfo.xml entry — page images are NOT re-read or re-compressed (D-A2).
    ///
    /// Metadata source (D-C2): a MINIMAL <see cref="ReleaseInfo"/> reconstructed from the persisted
    /// <see cref="ChapterFile.ScanlationGroup"/> / <see cref="ChapterFile.TranslatedLanguage"/>
    /// (these already carry the <c>lc.ScanlationGroup ?? lc.Release?.ScanlationGroup ?? lc.Release?.Indexer</c>
    /// resolution chain applied at import-step 3). <c>&lt;PageCount&gt;</c> is the REAL image-entry
    /// count (D-C1) — <see cref="ComicInfoXmlBuilder"/> emits <c>&lt;PageCount&gt;0&lt;/PageCount&gt;</c>
    /// even at 0, so we must count actual pages.
    ///
    /// Failure ladder (D-B): the upsert is wrapped in a try. On the FIRST failure we log
    /// <c>Warn</c> and retry once after a SHORT delay (<see cref="RetryDelayMs"/> ms — the
    /// AV / cloud-sync / Windows-Search file-lock window; Pitfall 4 — never block long, the import
    /// loop is synchronous per-decision). If the retry ALSO fails the exception RE-THROWS (fatal)
    /// so the caller's existing per-decision try/catch publishes <c>ChapterImportFailedEvent</c>
    /// (D-B1 — this injector NEVER publishes an event itself; no duplicate publish).
    ///
    /// D-D: only the <c>.cbz</c> path exists (cbt / folder deferred). Assumes a <c>.cbz</c> at
    /// <see cref="ChapterFile.Path"/>.
    /// </summary>
    public class ComicInfoCbzInjector : IComicInfoCbzInjector
    {
        // Short file-lock retry window (Pitfall 4). Sub-second — the import loop is synchronous
        // per-decision so this MUST NOT block long.
        private const int RetryDelayMs = 500;

        private static readonly string[] ImageExtensions =
        {
            ".jpg", ".jpeg", ".png", ".webp", ".gif", ".avif",
        };

        private readonly Logger _logger;

        public ComicInfoCbzInjector(Logger logger)
        {
            _logger = logger;
        }

        public void Inject(ChapterFile chapterFile, NzbDrone.Core.Manga.Manga manga, Chapter chapter, string gatewaySource = null)
        {
            try
            {
                Upsert(chapterFile, manga, chapter, gatewaySource);
            }
            catch (Exception ex)
            {
                // D-B — first failure: Warn + short delay + retry once. The transient cause is a
                // brief file lock (AV scan / cloud-sync / Windows Search indexer) on the just-moved CBZ.
                _logger.Warn(ex, "ComicInfo injection failed for {0}; retrying once after {1}ms", chapterFile?.Path, RetryDelayMs);
                Thread.Sleep(TimeSpan.FromMilliseconds(RetryDelayMs));

                // Second attempt — if THIS throws, the exception propagates (fatal, D-B1). The
                // injector deliberately does NOT swallow a persisted failure and does NOT publish
                // any event; the caller's existing per-decision catch publishes ChapterImportFailedEvent.
                Upsert(chapterFile, manga, chapter, gatewaySource);
            }
        }

        private void Upsert(ChapterFile chapterFile, NzbDrone.Core.Manga.Manga manga, Chapter chapter, string gatewaySource)
        {
            // D-A2 — open the FINISHED CBZ with Update (not Create); do NOT re-compress pages.
            using var zip = ZipFile.Open(chapterFile.Path, ZipArchiveMode.Update);

            // D-C1 — PageCount is the REAL image-entry count (excludes the ComicInfo.xml entry).
            var pageCount = zip.Entries.Count(e => IsImageEntry(e.Name));

            // D-C2 — minimal ReleaseInfo from the persisted ChapterFile fields (already carry the
            // group/language resolution chain). Do NOT re-source from a raw release.
            var release = new ReleaseInfo
            {
                TranslatedLanguage = chapterFile.TranslatedLanguage,
                ScanlationGroup = chapterFile.ScanlationGroup,

                // The completed download's originating gateway source (comix / kagane / mangadex);
                // ComicInfoXmlBuilder records it in <Notes>. Null for non-gateway imports.
                Source = gatewaySource,
            };

            var request = new ChapterArchiveRequest
            {
                Manga = manga,
                Chapter = chapter,
                Release = release,
                PageCount = pageCount,
            };

            // Reuse the pure builder verbatim (MUST survive Phase 39) — no new XML-construction code.
            var xml = ComicInfoXmlBuilder.Build(request);

            // Delete-then-create upsert (D-A2) — guarantees a single ComicInfo.xml entry.
            zip.GetEntry("ComicInfo.xml")?.Delete();
            var entry = zip.CreateEntry("ComicInfo.xml");
            using var stream = entry.Open();
            xml.Save(stream);
        }

        // Image-entry filter (D-C1): any entry with a known image extension, excluding ComicInfo.xml.
        private static bool IsImageEntry(string name)
        {
            if (string.IsNullOrEmpty(name) ||
                string.Equals(name, "ComicInfo.xml", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var ext = Path.GetExtension(name);
            return ImageExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
        }
    }
}
