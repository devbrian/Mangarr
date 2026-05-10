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
    /// Phase 16.1 Wave 3 (REVERT-03): chapter-feed return shape reverted to
    /// Sonarr-canonical flat <see cref="IEnumerable{T}"/> of <see cref="Chapter"/> — one
    /// row per canonical chapter. Mirror of Sonarr's <c>IProvideSeriesInfo.GetSeriesInfo</c>
    /// returning <c>Tuple&lt;Series, IEnumerable&lt;Episode&gt;&gt;</c>. Per-translation
    /// language and scanlation-group axes live on <see cref="MediaFiles.ChapterFile"/>
    /// after import (Phase 6 PIPELINE-04 axis), not on the metadata-feed projection.
    /// </para>
    /// </summary>
    public interface IProvideMangaInfo
    {
        Tuple<Manga.Manga, IEnumerable<Chapter>> GetMangaInfo(string sourceId);
    }
}
