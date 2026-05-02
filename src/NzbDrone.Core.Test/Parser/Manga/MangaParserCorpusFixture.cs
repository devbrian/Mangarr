using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Parser.Manga
{
    /// <summary>
    /// Phase 2 corpus runner. Loads the 500-title MangaDex /chapter feed corpus from disk
    /// and asserts that the manga parser produces a non-null parse for at least 95% of
    /// entries (D-08). Fixture is RED until Plan 02-04 lands MangaParser.cs.
    ///
    /// Per RESEARCH.md Pitfall 8 the corpus is committed in-tree, not regenerated at test
    /// time. Manual refresh: scripts/regenerate-manga-corpus.ps1.
    /// </summary>
    [TestFixture]
    public class MangaParserCorpusFixture : CoreTest
    {
        [Test]
        public void Parses_at_least_95_percent_of_500()
        {
            var corpusPath = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "Parser",
                "Manga",
                "test_corpus_v1.json");

            File.Exists(corpusPath).Should().BeTrue($"corpus file must be deployed to {corpusPath}");

            var json = File.ReadAllText(corpusPath);
            var entries = JsonConvert.DeserializeObject<List<CorpusEntry>>(json);

            entries.Count.Should().Be(500, "D-07 mandates exactly 500 entries");

            var parsed = entries.Count(e =>
                MangaParser.ParseChapterTitle(e.ReleaseTitle) != null);

            var parseRate = parsed / 500.0;

            Assert.That(
                parseRate,
                Is.GreaterThanOrEqualTo(0.95),
                $"Corpus parse-rate {parseRate:P} below 95% gate (D-08); parsed={parsed}/500");
        }

        private class CorpusEntry
        {
            [JsonProperty("release_title")]
            public string ReleaseTitle { get; set; }

            [JsonProperty("expected_chapter_numbers")]
            public decimal[] ExpectedChapterNumbers { get; set; }

            [JsonProperty("expected_volume")]
            public int? ExpectedVolume { get; set; }

            [JsonProperty("expected_chapter_type")]
            public string ExpectedChapterType { get; set; }

            [JsonProperty("expected_translated_language")]
            public string ExpectedTranslatedLanguage { get; set; }

            [JsonProperty("expected_scanlation_group")]
            public string ExpectedScanlationGroup { get; set; }

            [JsonProperty("expected_title")]
            public string ExpectedTitle { get; set; }
        }
    }
}
