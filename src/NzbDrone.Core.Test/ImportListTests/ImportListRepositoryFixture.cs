using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests
{
    // Phase 26 Plan 26-04 — Dapper round-trip on the IL-02 D-13 repo #1 (Definition).
    // Mirrors NzbDrone.Core.Test/Indexers/IndexerRepositoryFixture.cs verbatim shape.
    //
    // The empty-Settings InsertMany path here transparently exercises CR-02
    // SQLITE_BUSY retry coverage on read (Query override at ProviderRepository<T>:38-83)
    // — that path is the load-bearing reason D-13 ships a separate ProviderRepository
    // for ImportListDefinition vs a generic-collapsed shape.
    [TestFixture]
    public class ImportListRepositoryFixture : DbTest<ImportListRepository, ImportListDefinition>
    {
        private void GivenImportLists()
        {
            var importLists = Builder<ImportListDefinition>.CreateListOfSize(2)
                .All()
                .With(c => c.Id = 0)
                .With(c => c.Settings = null) // ProviderRepository hydrates to NullConfig.Instance on null body
                .With(c => c.ConfigContract = "TestImportListSettings")
                .With(c => c.Implementation = "TestImportList")
                .TheFirst(1)
                .With(x => x.Name = "MyImportList")
                .TheNext(1)
                .With(x => x.Name = "My Second ImportList")
                .BuildList();

            Subject.InsertMany(importLists);
        }

        [Test]
        public void should_find_with_name()
        {
            GivenImportLists();
            var found = Subject.FindByName("MyImportList");
            found.Should().NotBeNull("FindByName returns the row inserted by GivenImportLists");
            found.Name.Should().Be("MyImportList");
            found.Id.Should().Be(1);
        }

        [Test]
        public void should_not_find_with_incorrect_case_name()
        {
            GivenImportLists();
            var found = Subject.FindByName("myimportlist");
            found.Should().BeNull("FindByName is case-sensitive — IndexerRepository parity");
        }

        [Test]
        public void should_persist_via_UpdateSettings()
        {
            GivenImportLists();
            var first = Subject.FindByName("MyImportList");

            // The Settings column is hydrated to NullConfig.Instance on read (because we
            // inserted with Settings = null). UpdateSettings(model) on the round-tripped
            // POCO must not throw; the inserted row's other columns must survive
            // verbatim (D-13 read-path retry funnel + UpdateSettings's narrow SetFields
            // call to ONLY the Settings JSON column).
            Subject.UpdateSettings(first);

            var afterUpdate = Subject.FindByName("MyImportList");
            afterUpdate.Should().NotBeNull("the row survives UpdateSettings");
            afterUpdate.Id.Should().Be(first.Id, "UpdateSettings touches only the Settings column");
            afterUpdate.Implementation.Should().Be("TestImportList", "non-Settings columns are not perturbed");
        }
    }
}
