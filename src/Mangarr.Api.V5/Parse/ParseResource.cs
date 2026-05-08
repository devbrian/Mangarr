using NzbDrone.Core.Languages;
using NzbDrone.Core.Parser.Model;
using Mangarr.Api.V5.CustomFormats;
using Mangarr.Api.V5.Episodes;
using Mangarr.Api.V5.Series;
using Sonarr.Http.REST;

namespace Mangarr.Api.V5.Parse;

public class ParseResource : RestResource
{
    public string? Title { get; set; }
    public ParsedEpisodeInfo? ParsedEpisodeInfo { get; set; }
    public SeriesResource? Series { get; set; }
    public List<EpisodeResource>? Episodes { get; set; }
    public List<Language>? Languages { get; set; }
    public List<CustomFormatResource>? CustomFormats { get; set; }
    public int CustomFormatScore { get; set; }
}
