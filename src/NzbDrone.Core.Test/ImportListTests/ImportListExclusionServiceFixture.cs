using System;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.Manga.Events;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests
{
    // Phase 26 Plan 26-04 (D-12 / Open Q #1) — ImportListExclusionService.Handle
    // (MangaDeletedEvent) behavior coverage. Each test names the exact behavior under
    // assertion per feedback_verify_ui_state_not_just_rendering.
    [TestFixture]
    public class ImportListExclusionServiceFixture : CoreTest<ImportListExclusionService>
    {
        private static readonly Guid SampleMangaDexId = new Guid("33333333-3333-3333-3333-333333333333");

        private Manga.Manga GivenManga(Guid? mangaDexId = null, int? malId = null, int? aniListId = null, int? mangaBakaId = null, string title = "Test Manga")
        {
            return new Manga.Manga
            {
                Id = 1,
                MangaDexId = mangaDexId ?? SampleMangaDexId,
                MalId = malId,
                AniListId = aniListId,
                MangaBakaId = mangaBakaId,
                Title = title
            };
        }

        [Test]
        public void handle_inserts_exclusion_when_flag_true_and_no_existing_row()
        {
            var manga = GivenManga();
            var evt = new MangaDeletedEvent(manga, deleteFiles: false, addImportListExclusion: true);

            Mocker.GetMock<IImportListExclusionRepository>()
                  .Setup(r => r.FindByMangaDexId(It.IsAny<string>()))
                  .Returns((ImportListExclusion)null);

            Subject.Handle(evt);

            Mocker.GetMock<IImportListExclusionRepository>()
                  .Verify(r => r.Insert(It.Is<ImportListExclusion>(e =>
                      e.MangaDexId == SampleMangaDexId.ToString() &&
                      e.Title == "Test Manga")),
                      Times.Once,
                      "default-true AddImportListExclusion + no existing row → exactly one Insert");
        }

        [Test]
        public void handle_short_circuits_when_AddImportListExclusion_false()
        {
            var manga = GivenManga();
            var evt = new MangaDeletedEvent(manga, deleteFiles: false, addImportListExclusion: false);

            Subject.Handle(evt);

            Mocker.GetMock<IImportListExclusionRepository>()
                  .Verify(r => r.Insert(It.IsAny<ImportListExclusion>()),
                      Times.Never,
                      "AddImportListExclusion=false → handler must early-return without touching the repo");
            Mocker.GetMock<IImportListExclusionRepository>()
                  .Verify(r => r.FindByMangaDexId(It.IsAny<string>()),
                      Times.Never,
                      "AddImportListExclusion=false short-circuits BEFORE the FindByMangaDexId lookup");
        }

        [Test]
        public void handle_short_circuits_when_existing_exclusion_found_via_FindByMangaDexId()
        {
            var manga = GivenManga();
            var evt = new MangaDeletedEvent(manga, deleteFiles: false, addImportListExclusion: true);

            Mocker.GetMock<IImportListExclusionRepository>()
                  .Setup(r => r.FindByMangaDexId(SampleMangaDexId.ToString()))
                  .Returns(new ImportListExclusion { Id = 7, MangaDexId = SampleMangaDexId.ToString(), Title = "Test Manga" });

            Subject.Handle(evt);

            Mocker.GetMock<IImportListExclusionRepository>()
                  .Verify(r => r.Insert(It.IsAny<ImportListExclusion>()),
                      Times.Never,
                      "idempotency guard: existing exclusion row → handler must NOT insert a duplicate (UNIQUE index would reject it anyway)");
        }

        [Test]
        public void handle_persists_full_quad_MangaDexId_MalId_AniListId_MangaBakaId()
        {
            var manga = GivenManga(malId: 555, aniListId: 777, mangaBakaId: 900);
            var evt = new MangaDeletedEvent(manga, deleteFiles: false, addImportListExclusion: true);

            Mocker.GetMock<IImportListExclusionRepository>()
                  .Setup(r => r.FindByMangaDexId(It.IsAny<string>()))
                  .Returns((ImportListExclusion)null);

            ImportListExclusion captured = null;
            Mocker.GetMock<IImportListExclusionRepository>()
                  .Setup(r => r.Insert(It.IsAny<ImportListExclusion>()))
                  .Callback<ImportListExclusion>(e => captured = e)
                  .Returns<ImportListExclusion>(e => e);

            Subject.Handle(evt);

            captured.Should().NotBeNull("Insert was called");
            captured.MangaDexId.Should().Be(SampleMangaDexId.ToString(), "MangaDexId is serialized as the canonical Guid string");
            captured.MalId.Should().Be(555, "the MAL quad member persists");
            captured.AniListId.Should().Be(777, "the AniList quad member persists");
            captured.MangaBakaId.Should().Be(900, "the MangaBaka quad member (v1.3 default-primary anchor) persists");
            captured.Title.Should().Be("Test Manga");
        }

        [Test]
        public void handle_inserts_row_for_mangabaka_only_manga()
        {
            // Regression (quick-260619-spc): a manga carrying ONLY a MangaBakaId
            // (the v1.3 default-primary anchor) — MangaDexId/MalId/AniListId all null —
            // was previously dropped by the all-null skip guard, so no exclusion row
            // was ever created and an import list could re-add the deleted title.
            var manga = new Manga.Manga { Id = 1, MangaDexId = null, MalId = null, AniListId = null, MangaBakaId = 900, Title = "MangaBaka-Only" };
            var evt = new MangaDeletedEvent(manga, deleteFiles: false, addImportListExclusion: true);

            Mocker.GetMock<IImportListExclusionRepository>()
                  .Setup(r => r.All())
                  .Returns(new ImportListExclusion[] { });

            ImportListExclusion captured = null;
            Mocker.GetMock<IImportListExclusionRepository>()
                  .Setup(r => r.Insert(It.IsAny<ImportListExclusion>()))
                  .Callback<ImportListExclusion>(e => captured = e)
                  .Returns<ImportListExclusion>(e => e);

            Subject.Handle(evt);

            Mocker.GetMock<IImportListExclusionRepository>()
                  .Verify(r => r.Insert(It.IsAny<ImportListExclusion>()),
                      Times.Once,
                      "a MangaBaka-only delete must now create exactly one exclusion row");
            captured.Should().NotBeNull();
            captured.MangaBakaId.Should().Be(900, "the MangaBaka-only row keys on MangaBakaId");
            captured.MangaDexId.Should().BeNull();
        }

        [Test]
        public void handle_does_not_duplicate_when_existing_exclusion_matches_mangabaka_id()
        {
            // Idempotency: an existing exclusion row already covers this manga's
            // MangaBakaId (and there is no MangaDexId match), so the handler must
            // NOT insert a duplicate.
            var manga = new Manga.Manga { Id = 1, MangaDexId = null, MalId = null, AniListId = null, MangaBakaId = 900, Title = "MangaBaka-Only" };
            var evt = new MangaDeletedEvent(manga, deleteFiles: false, addImportListExclusion: true);

            Mocker.GetMock<IImportListExclusionRepository>()
                  .Setup(r => r.All())
                  .Returns(new[] { new ImportListExclusion { Id = 5, MangaBakaId = 900, Title = "MangaBaka-Only" } });

            Subject.Handle(evt);

            Mocker.GetMock<IImportListExclusionRepository>()
                  .Verify(r => r.Insert(It.IsAny<ImportListExclusion>()),
                      Times.Never,
                      "idempotency: an existing MangaBakaId-keyed exclusion must not be duplicated on re-delete");
        }

        [Test]
        public void handle_skips_when_all_four_ids_are_null_or_zero()
        {
            var manga = new Manga.Manga { Id = 99, MangaDexId = null, MalId = null, AniListId = null, MangaBakaId = null, Title = "Nothing-To-Exclude" };
            var evt = new MangaDeletedEvent(manga, deleteFiles: false, addImportListExclusion: true);

            Subject.Handle(evt);

            Mocker.GetMock<IImportListExclusionRepository>()
                  .Verify(r => r.Insert(It.IsAny<ImportListExclusion>()),
                      Times.Never,
                      "all-null-quad row has no key for any finder to reach later — skip rather than store an unreachable exclusion");
        }
    }
}
