using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.MediaFiles.ChapterArchiving;

namespace NzbDrone.Core.Metadata.Stax
{
    // quick-260701-e71 — the FIRST on-disk SERIES-LEVEL Metadata output. Writes a
    // stax.json = { mangabakaId } file into each manga's on-disk folder on Refresh Manga
    // (and on first add — MangaAddedHandler funnels adds through RefreshMangaCommand, so
    // RefreshMangaService is the single hook covering both).
    //
    // This reintroduces the Sonarr-canonical series-level on-disk write shape that R-14
    // (Phase 30) deferred — Mangarr's only prior IMetadata provider, ComicInfo, is
    // CBZ-internal. Stax overrides the optional IMetadata.WriteMangaMetadata(Manga) added
    // for this change; ComicInfo leaves it the MetadataBase no-op.
    //
    // Auto-discovered by DryIoc via the IMetadata ThingiProvider contract — NO manual DI
    // registration. MetadataFactory.InitializeProviders auto-seeds a DISABLED default row on
    // ApplicationStartedEvent (D-01: opt-in, no migration); the user enables it in
    // Settings -> Metadata.
    public class StaxMetadata : MetadataBase<StaxMetadataSettings>
    {
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public StaxMetadata(IDiskProvider diskProvider, Logger logger)
        {
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public override string Name => "Stax";

        public override string FormatKey => "stax";

        // Stax is NOT a CBZ-internal writer — CBZ archivers must never invoke this provider
        // to emit an in-archive sidecar. It writes a series-level stax.json on-disk via
        // WriteMangaMetadata instead. Returning false keeps it out of the archiver's
        // enabled-writer enumeration.
        public override bool AppliesTo(ChapterArchiveRequest request) => false;

        // No CBZ-internal sidecar — see AppliesTo. The series-level write lives in
        // WriteMangaMetadata below.
        public override Task WriteAsync(ChapterArchiveRequest request, ArchiveOutputContext ctx, CancellationToken ct) => Task.CompletedTask;

        // On-disk series-level write with the D-02 self-heal + D-03 skip semantics.
        public override void WriteMangaMetadata(NzbDrone.Core.Manga.Manga manga)
        {
            // D-03 skip: no manga / no on-disk folder / no MangaBakaId yet -> write nothing,
            // no error. The write reappears on a later refresh once an id resolves. Short-circuit
            // BEFORE touching the disk provider (FileExists) so the null-id + empty-path cases
            // never reach disk.
            if (manga == null || string.IsNullOrEmpty(manga.Path) || manga.MangaBakaId == null)
            {
                _logger.Trace("Skipping stax.json write: manga null, Path empty, or no MangaBakaId");
                return;
            }

            var path = System.IO.Path.Combine(manga.Path, "stax.json");
            var payload = new StaxPayload { MangaBakaId = manga.MangaBakaId.Value };
            var json = Json.ToJson(payload);

            // D-02 self-heal compare: if a stax.json already exists and its stored mangabakaId
            // matches the manga's current MangaBakaId, do nothing (no write). Otherwise (missing,
            // stale, or unparseable) fall through and (over)write.
            if (_diskProvider.FileExists(path))
            {
                var existing = _diskProvider.ReadAllText(path);
                if (Json.TryDeserialize<StaxPayload>(existing, out var parsed)
                    && parsed != null
                    && parsed.MangaBakaId == manga.MangaBakaId.Value)
                {
                    _logger.Trace("stax.json unchanged for manga {0} (mangabakaId {1})", manga.Title, manga.MangaBakaId.Value);
                    return;
                }
            }

            try
            {
                _diskProvider.WriteAllText(path, json);
                _logger.Debug("Wrote stax.json for manga {0} (mangabakaId {1})", manga.Title, manga.MangaBakaId.Value);
            }
            catch (Exception e)
            {
                // WR-07 batch tolerance: a single bad stax write must not abort the surrounding
                // refresh batch (mirrors the RescanManga catch-and-continue invariant).
                _logger.Warn(e, "Couldn't write stax.json for manga {0}", manga.Title);
            }
        }

        // Tiny on-disk shape: exactly one property, serialized camelCase to `mangabakaId` by
        // the CamelCasePropertyNamesContractResolver in Json.GetSerializerSettings().
        private sealed class StaxPayload
        {
            public int MangaBakaId { get; set; }
        }
    }
}
