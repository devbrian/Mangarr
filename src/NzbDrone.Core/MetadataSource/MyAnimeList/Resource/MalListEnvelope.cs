using System.Collections.Generic;

namespace NzbDrone.Core.MetadataSource.MyAnimeList.Resource
{
    /// <summary>
    /// MAL v2 list-response envelope: <c>{ "data": [{ "node": T }, ...] }</c>. Search and
    /// related endpoints wrap each item in a <c>node</c> sub-object.
    /// </summary>
    public class MalListEnvelope<T>
    {
        public List<MalNodeWrapper<T>> Data { get; set; }
    }

    public class MalNodeWrapper<T>
    {
        public T Node { get; set; }
    }
}
