using System;
using System.Collections.Generic;
using NzbDrone.Core.Manga;

namespace NzbDrone.Core.MetadataSource
{
    /// <summary>
    /// Phase 2 split-contract per D-14 — provides metadata + chapter list for an existing
    /// known source ID. Mirrors Sonarr's <c>IProvideSeriesInfo</c> (DELETED Phase 15) concrete-singleton
    /// precedent but registered as a ThingiProvider family (each implementation is a
    /// pluggable <c>MetadataSourceBase&lt;TSettings&gt;</c>).
    ///
    /// The string sourceId accepts:
    ///   - MangaDex GUID (string form, e.g. "8e0a2e9c-...") for MangaDexMetadataSource
    ///   - AniList integer (string form, e.g. "30013") for AniListMetadataSource
    ///   - MAL integer (string form, e.g. "11") for MyAnimeListMetadataSource
    /// Each provider parses its native form and throws <see cref="MangaNotFoundException"/>
    /// on 404.
    /// </summary>
    public interface IProvideMangaInfo
    {
        Tuple<Manga.Manga, List<Chapter>> GetMangaInfo(string sourceId);
    }
}
