using System;
using System.Collections.Generic;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Indexers.Http;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Test.IndexerTests
{
    /// <summary>
    /// Test stub implementing <see cref="IHttpAggregatorSettings"/>. Exposes Rate as a
    /// directly-settable property (no FieldDefinition / RateSeconds bridging) so unit tests can
    /// inject a TimeSpan? value without going through the production POCO's serialization shape.
    /// </summary>
    public class TestHttpAggregatorSettings : IHttpAggregatorSettings
    {
        [FieldDefinition(0, Label = "URL")]
        public string BaseUrl { get; set; } = "http://example.test";

        public string SourceKey { get; set; }

        public TimeSpan? Rate { get; set; }

        public string UserAgentOverride { get; set; }

        public IEnumerable<int> MultiLanguages { get; set; } = Array.Empty<int>();

        public IEnumerable<int> FailDownloads { get; set; } = Array.Empty<int>();

        public NzbDroneValidationResult Validate() => new NzbDroneValidationResult();
    }
}
