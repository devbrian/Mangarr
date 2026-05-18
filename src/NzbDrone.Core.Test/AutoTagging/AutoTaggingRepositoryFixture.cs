using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.AutoTagging
{
    // Phase 24 v1.1 Wave-0 — AutoTaggingRepository round-trip coverage.
    // Mirrors MangaRepositoryFixture's DbTest<TRepo, TModel> shape (existing
    // baseline harness — the AutoTagging table is already present in
    // 001_mangarr_baseline.cs:303-307 so no migration setup required for the
    // round-trip tests).
    [TestFixture]
    public class AutoTaggingRepositoryFixture : DbTest<AutoTaggingRepository, AutoTag>
    {
        private AutoTag BuildAutoTag(string name = "Test Rule", HashSet<int> tags = null)
        {
            return new AutoTag
            {
                Name = name,
                Tags = tags ?? new HashSet<int>(),
                Specifications = new List<global::NzbDrone.Core.AutoTagging.Specifications.IAutoTaggingSpecification>(),
            };
        }

        [Test]
        public void Insert_returns_id()
        {
            var autoTag = BuildAutoTag();
            Subject.Insert(autoTag);
            autoTag.Id.Should().BeGreaterThan(0);
        }

        [Test]
        public void Get_by_id_returns_inserted()
        {
            var autoTag = BuildAutoTag(name: "Round-trip rule");
            Subject.Insert(autoTag);

            var fetched = Subject.Get(autoTag.Id);

            fetched.Should().NotBeNull();
            fetched.Name.Should().Be("Round-trip rule");
        }

        [Test]
        public void Update_persists_changes()
        {
            var autoTag = BuildAutoTag();
            Subject.Insert(autoTag);

            autoTag.Name = "Updated Name";
            autoTag.RemoveTagsAutomatically = true;
            autoTag.Tags = new HashSet<int> { 1, 2, 3 };
            Subject.Update(autoTag);

            var fetched = Subject.Get(autoTag.Id);
            fetched.Name.Should().Be("Updated Name");
            fetched.RemoveTagsAutomatically.Should().BeTrue();
            fetched.Tags.Should().BeEquivalentTo(new[] { 1, 2, 3 });
        }

        [Test]
        public void Delete_removes_row()
        {
            var autoTag = BuildAutoTag();
            Subject.Insert(autoTag);

            Subject.Delete(autoTag.Id);

            Subject.All().Should().BeEmpty();
        }

        [Test]
        public void All_returns_all_rules()
        {
            Subject.Insert(BuildAutoTag(name: "Rule A"));
            Subject.Insert(BuildAutoTag(name: "Rule B"));

            Subject.All().ToList().Should().HaveCount(2);
        }

        [Test]
        public void Tags_HashSet_round_trips_via_IEmbeddedDocumentConverter()
        {
            var autoTag = BuildAutoTag(name: "Tagged rule", tags: new HashSet<int> { 42, 7, 99 });
            Subject.Insert(autoTag);

            var fetched = Subject.Get(autoTag.Id);
            fetched.Tags.Should().BeEquivalentTo(new[] { 42, 7, 99 });
        }
    }
}
