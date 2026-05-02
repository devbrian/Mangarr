using System.Collections.Generic;

namespace NzbDrone.Core.MetadataSource.MyAnimeList.Resource
{
    /// <summary>
    /// MAL v2 manga resource (subset of the full payload — only the fields requested via
    /// <c>MalApi.MangaFields</c>). Field mapping mirrors RESEARCH §Code Examples Pattern 4
    /// lines 1162-1173. The D-21 cross-source resolver axes are populated from:
    ///   * <c>StartDate</c> (first 4 chars = publication year)
    ///   * <c>NumChapters</c> (total-chapter-count axis)
    ///   * <c>Authors[].Role == "Story"</c> (primary-author axis)
    /// </summary>
    public class MalMangaResource
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public AlternativeTitlesBlock AlternativeTitles { get; set; }
        public MainPicture MainPicture { get; set; }
        public string StartDate { get; set; }      // ISO date; first 4 chars = year (D-21 publication-year axis)
        public string EndDate { get; set; }
        public string Synopsis { get; set; }
        public string Status { get; set; }         // currently_publishing | finished | ...
        public int? NumVolumes { get; set; }
        public int? NumChapters { get; set; }      // total-chapter-count axis (D-21)
        public List<MalAuthorEdge> Authors { get; set; }   // role="Story" → primary-author axis
        public List<MalGenre> Genres { get; set; }
        public string Nsfw { get; set; }           // white | gray | black
    }

    public class AlternativeTitlesBlock
    {
        public List<string> Synonyms { get; set; }
        public string En { get; set; }
        public string Ja { get; set; }
    }

    public class MainPicture
    {
        public string Medium { get; set; }
        public string Large { get; set; }
    }

    public class MalAuthorEdge
    {
        public MalAuthorNode Node { get; set; }
        public string Role { get; set; }
    }

    public class MalAuthorNode
    {
        public int Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
    }

    public class MalGenre
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
}
