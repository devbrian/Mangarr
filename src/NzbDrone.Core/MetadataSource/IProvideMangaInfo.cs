using System;
using System.Collections.Generic;
using NzbDrone.Core.Manga;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Phase 2 split-contract per D-14 — provides metadata + chapter list for an existing
    /// known source ID. Mirrors Mangarr's <c>IProvideSeriesInfo</c> (DELETED Phase 15) concrete-singleton
    /// precedent but registered as a ThingiProvider family (each implementation is a
    /// pluggable <c>MetadataSourceBase&lt;TSettings&gt;</c>).
    ///
    /// The string sourceId accepts:
    ///   - MangaDex GUID (string form, e.g. "8e0a2e9c-...") for MangaDexMetadataSource
    ///   - AniList integer (string form, e.g. "30013") for AniListMetadataSource
    ///   - MAL integer (string form, e.g. "11") for MyAnimeListMetadataSource
    /// Each provider parses its native form and throws <see cref="MangaNotFoundException"/>
    /// on 404.
    ///
    /// <para>
    /// Phase 16 STRUCT-05 + STRUCT-07: the chapter-feed return shape changed from a flat
    /// <c>List&lt;Chapter&gt;</c> (one row per chapter) to a tuple stream — one element per
    /// canonical chapter, each carrying a <see cref="Manga.ChapterEnsureInputs"/> (canonical
    /// upsert payload) + a <see cref="List{T}"/> of <see cref="Manga.ChapterReleaseFeedRow"/>
    /// (per-translation upload metadata). RefreshMangaService consumes this stream in a
    /// two-pass <c>EnsureChapter</c> + <c>SyncChapterReleases</c> shape (mirrors Sonarr's
    /// RefreshEpisodeService two-pass).
    /// </para>
    /// </summary>
    public interface IProvideMangaInfo
    {
        Tuple<Manga.Manga, IEnumerable<(decimal ChapterNumber, ChapterEnsureInputs Canonical, List<ChapterReleaseFeedRow> Releases)>>
            GetMangaInfo(string sourceId);
    }
}
