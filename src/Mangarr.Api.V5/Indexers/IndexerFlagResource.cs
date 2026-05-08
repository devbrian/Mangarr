using Newtonsoft.Json;
using Mangarr.Http.REST;

namespace Mangarr.Api.V5.Indexers;

public class IndexerFlagResource : RestResource
{
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
    public new int Id { get; set; }
    public string? Name { get; set; }
    public string? NameLower => Name?.ToLowerInvariant();
}
