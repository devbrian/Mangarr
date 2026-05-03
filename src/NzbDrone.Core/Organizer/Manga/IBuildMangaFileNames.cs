using System.Collections.Generic;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Parser.Model;
using MangaModel = NzbDrone.Core.Manga.Manga;

namespace NzbDrone.Core.Organizer.Manga
{
    // Sonarr divergence: NEW manga-side filename builder interface per Phase 5 D-14 — see DIVERGENCE.md.
    // Mirrors IBuildFileNames (TV) shape but operates on Chapter / Manga / ReleaseInfo (no
    // ChapterFile entity exists in v1 — Phase 4 archiver writes via IChapterArchiver and
    // returns the on-disk path; the file artifact model lands when Phase 6 wires manga
    // import dispatch). Using ReleaseInfo as the third parameter so token resolution
    // reads {ScanlationGroup}, {Language}, {Source} from indexer-supplied fields.
    // Phase 8 collapse: rename to canonical IBuildFileNames when Tv/ deletes.
    public interface IBuildMangaFileNames
    {
        string BuildFileName(
            List<NzbDrone.Core.Manga.Chapter> chapters,
            MangaModel manga,
            ReleaseInfo release = null,
            string extension = "",
            NamingConfig namingConfig = null,
            List<CustomFormat> customFormats = null);

        string BuildFilePath(
            List<NzbDrone.Core.Manga.Chapter> chapters,
            MangaModel manga,
            ReleaseInfo release,
            string extension,
            NamingConfig namingConfig = null,
            List<CustomFormat> customFormats = null);

        string GetMangaFolder(MangaModel manga, NamingConfig namingConfig = null);
    }
}
