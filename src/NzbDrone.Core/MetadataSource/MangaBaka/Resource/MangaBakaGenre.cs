using System.Collections.Generic;
using Newtonsoft.Json;

namespace NzbDrone.Core.MetadataSource.MangaBaka.Resource
{
    /// <summary>
    /// A single genre option from GET <c>/v1/genres</c> (DISC-03). MangaBaka ships each genre as a
    /// <c>{ label, value }</c> pair — the human-facing <see cref="Label"/> (e.g. "Boys' Love") and
    /// the API filter token <see cref="Value"/> (e.g. "boys_love") that binds to the
    /// <c>genre</c> / <c>genre_not</c> browse params.
    ///
    /// Snake_case-safe via explicit <c>[JsonProperty]</c> attributes (the project-wide Newtonsoft
    /// contract resolver is camelCase, but MangaBaka ships snake_case keys).
    /// </summary>
    public class MangaBakaGenre
    {
        [JsonProperty("label")]
        public string Label { get; set; }

        [JsonProperty("value")]
        public string Value { get; set; }
    }

    /// <summary>
    /// Envelope for GET <c>/v1/genres</c> — MangaBaka wraps the option list in <c>{ data[] }</c>.
    /// </summary>
    public class MangaBakaGenreListResource
    {
        [JsonProperty("data")]
        public List<MangaBakaGenre> Data { get; set; }
    }
}
