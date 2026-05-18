using System.Collections.Generic;
using NzbDrone.Core.AutoTagging.Specifications;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.AutoTagging
{
    public class AutoTag : ModelBase
    {
        public AutoTag()
        {
            // Mangarr divergence from Sonarr `6f857ba0e^`: initialize Specifications
            // alongside Tags so downstream consumers (AutoTaggingService.GetTagChanges
            // line 102, V5 AutoTaggingController.SharedValidator) never NRE on a
            // never-touched-by-caller AutoTag instance. Upstream relies on the API
            // validator catching the empty case before the service is reached;
            // we add the same guard at construction for defense-in-depth.
            Specifications = new List<IAutoTaggingSpecification>();
            Tags = new HashSet<int>();
        }

        public string Name { get; set; }
        public List<IAutoTaggingSpecification> Specifications { get; set; }
        public bool RemoveTagsAutomatically { get; set; }
        public HashSet<int> Tags { get; set; }
    }
}
