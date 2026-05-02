using System.Collections.Generic;

namespace NzbDrone.Core.MetadataSource.AniList.Resource
{
    /// <summary>
    /// AniList <c>Media</c> DTO — populated from the <see cref="AniListMangaApi.MediaByIdQuery"/>
    /// + sibling search/idMal queries. Only the fields actually requested in those queries are
    /// modeled (Newtonsoft tolerates missing properties either way; we list them for the
    /// resolver + mapper consumers in Plan 02-07 / 02-09).
    ///
    /// Cross-source axes (D-21 multi-axis confirm):
    ///   - <see cref="IdMal"/>            → cross-source MAL ID for D-19 reverse-lookup
    ///   - <see cref="Chapters"/>         → total-chapter-count axis (within 10% gate)
    ///   - <see cref="StartDate"/>.Year   → publication-year axis (within ±1 gate)
    ///   - <see cref="Staff"/>            → primary-author axis (role == "Story")
    /// </summary>
    public class AniListMedia
    {
        public int Id { get; set; }
        public int? IdMal { get; set; }                            // ← cross-source MAL ID (D-19 reverse path)
        public AniListTitleBlock Title { get; set; }
        public List<string> Synonyms { get; set; }
        public string Status { get; set; }                         // FINISHED | RELEASING | NOT_YET_RELEASED | CANCELLED | HIATUS
        public int? Chapters { get; set; }                         // total-chapter-count axis (D-21)
        public int? Volumes { get; set; }
        public AniListYmd StartDate { get; set; }                  // publication-year axis (D-21)
        public AniListYmd EndDate { get; set; }
        public string Description { get; set; }
        public AniListCoverImage CoverImage { get; set; }
        public string BannerImage { get; set; }
        public int? AverageScore { get; set; }
        public int? MeanScore { get; set; }
        public bool IsAdult { get; set; }
        public List<string> Genres { get; set; }
        public List<AniListTag> Tags { get; set; }
        public AniListStaffEdges Staff { get; set; }               // primary-author via role="Story" (D-21)
        public List<AniListExternalLink> ExternalLinks { get; set; }
    }

    public class AniListTitleBlock
    {
        public string UserPreferred { get; set; }
        public string Romaji { get; set; }
        public string Native { get; set; }
        public string English { get; set; }
    }

    public class AniListYmd
    {
        public int? Year { get; set; }
        public int? Month { get; set; }
        public int? Day { get; set; }
    }

    public class AniListCoverImage
    {
        public string ExtraLarge { get; set; }
        public string Large { get; set; }
        public string Medium { get; set; }
    }

    public class AniListTag
    {
        public string Name { get; set; }
        public int? Rank { get; set; }
    }

    public class AniListStaffEdges
    {
        public List<AniListStaffEdge> Edges { get; set; }
    }

    public class AniListStaffEdge
    {
        public string Role { get; set; }
        public AniListStaffNode Node { get; set; }
    }

    public class AniListStaffNode
    {
        public AniListStaffName Name { get; set; }
    }

    public class AniListStaffName
    {
        public string Full { get; set; }
        public string Native { get; set; }
    }

    public class AniListExternalLink
    {
        public string Site { get; set; }
        public string Url { get; set; }
    }
}
