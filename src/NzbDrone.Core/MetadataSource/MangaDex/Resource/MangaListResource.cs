using System.Collections.Generic;

namespace NzbDrone.Core.MetadataSource.MangaDex.Resource
{
    /// <summary>
    /// Envelope for GET /manga (search). MangaDex wraps every list response in
    /// <c>{ data: [...], limit, offset, total }</c>; we deserialize the
    /// <c>data</c> array only — <see cref="MangaDexApi.Search"/> caps page size at 10.
    ///
    /// Newtonsoft is configured project-wide with CamelCasePropertyNamesContractResolver
    /// (NzbDrone.Common/Serializer/Newtonsoft.Json/Json.cs:29), so <see cref="Data"/>
    /// (PascalCase) maps to <c>data</c> in the JSON payload.
    /// </summary>
    public class MangaListResource
    {
        public List<MangaDataItem> Data { get; set; }
    }
}
