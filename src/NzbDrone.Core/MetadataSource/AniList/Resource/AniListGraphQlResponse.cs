using System.Collections.Generic;

namespace NzbDrone.Core.MetadataSource.AniList.Resource
{
    /// <summary>
    /// Generic GraphQL envelope: every AniList response wraps its payload in <c>data</c>.
    /// Concrete <c>T</c> shapes (<see cref="MediaResponseShape"/>, <see cref="PageResponseShape"/>)
    /// match the field set requested in the corresponding <see cref="AniListMangaApi"/> query.
    /// </summary>
    public class AniListGraphQlResponse<T>
    {
        public T Data { get; set; }
    }

    /// <summary>
    /// Shape for <see cref="AniListMangaApi.MediaByIdQuery"/> + <see cref="AniListMangaApi.MediaByIdMalQuery"/>.
    /// </summary>
    public class MediaResponseShape
    {
        public AniListMedia Media { get; set; }
    }

    /// <summary>
    /// Shape for <see cref="AniListMangaApi.MediaSearchQuery"/> — paginated <c>Page.media[]</c>.
    /// </summary>
    public class PageResponseShape
    {
        public AniListPage Page { get; set; }
    }

    public class AniListPage
    {
        public AniListPageInfo PageInfo { get; set; }
        public List<AniListMedia> Media { get; set; }
    }

    public class AniListPageInfo
    {
        public int CurrentPage { get; set; }
        public int LastPage { get; set; }
        public bool HasNextPage { get; set; }
    }
}
