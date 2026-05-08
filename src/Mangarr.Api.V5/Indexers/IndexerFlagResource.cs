using Mangarr.Http.REST;
using Newtonsoft.Json;

namespace Mangarr.Api.V5.Indexers;

public class IndexerFlagResource : RestResource
{
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
    public new int Id { get; set; }
    public string? Name { get; set; }
    public string? NameLower => Name?.ToLowerInvariant();
}
