// Phase 40 Plan 40-03 (RECON-02/03/04): unit coverage for the NEW-in-Mangarr
// ChapterSynthesisService — the reconciliation engine that makes the local Chapter
// catalog mirror the external gateway. No Sonarr analog (TheTVDB defines both
// "what exists" and "what's grabbable"; the gateway exposes releases the MangaDex
// metadata catalog never enumerated).
//
// Behavior under test:
//   * Delta (RECON-02): on-search backfills the genuinely-missing contiguous WHOLE
//     range [1..maxWhole] minus already-cataloged numbers, plus Chapter 0 ONLY when a
//     chapter-0 release was actually attributed (no phantom Chapter 0).
//   * Fractional exclusion (RECON-02 / D-02): a 14.5 release is NEVER bulk-synthesized
//     on search.
//   * Attribution Killing-Field (RECON-02 / D-06/D-07): a release that does NOT belong
//     to the searched manga (ID mismatch AND non-exact title) synthesizes ZERO rows.
//   * Attribution ID-match (D-06): an ID-matched release with a fuzzy title IS attributed.
//   * Monitor policy (RECON-03 / D-05): None => Monitored=false; All / default(0) => true.
//   * No-clobber (RECON-04 / D-03): an already-cataloged number never reaches SyncChapters,
//     so the verbatim Title assignment in ChapterListService can never clobber a real title.
//   * On-grab (RECON-04 / D-04): SynthesizeForGrab synthesizes the single grabbed number
//     (whole OR fractional) when absent; no-ops when a row already exists.
using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine.Manga;
using NzbDrone.Core.Manga;
using NzbDrone.Core.Parser.Manga;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MangaTests
{
    [TestFixture]
    public class ChapterSynthesisServiceFixture : CoreTest<ChapterSynthesisService>
    {
        private Manga.Manga _searched;

        [SetUp]
        public void Setup()
        {
            _searched = new Manga.Manga
            {
                Id = 2,
                Title = "The Forgotten Field",
                MangaDexId = Guid.Parse("fb716acf-4257-4613-bf30-32d7c32dc199"),
                MalId = 555,
                AniListId = 777,
                MonitorNewItems = MangaMonitorNewItems.All,
            };

            // Default: no existing chapters cataloged.
            Mocker.GetMock<IChapterService>()
                .Setup(s => s.GetChaptersByManga(_searched.Id))
                .Returns(new List<Chapter>());

            // Default attribution: exact-title match resolves to the searched manga.
            Mocker.GetMock<IMangaService>()
                .Setup(s => s.FindByTitle(It.IsAny<string>()))
                .Returns(_searched);
        }

        private static MangaDownloadDecision Decision(
            string mangaTitle,
            decimal[] chapterNumbers,
            Dictionary<string, object> ids = null,
            DateTime? publishDate = null)
        {
            var parsed = new ParsedChapterInfo
            {
                MangaTitle = mangaTitle,
                ChapterNumbers = chapterNumbers ?? Array.Empty<decimal>(),
                ChapterType = ChapterType.Regular,
            };

            var release = new ReleaseInfo
            {
                Title = mangaTitle,
                Ids = ids,
            };

            if (publishDate.HasValue)
            {
                release.PublishDate = publishDate.Value;
            }

            var remote = new RemoteChapter
            {
                ParsedChapterInfo = parsed,
                Release = release,
            };

            return new MangaDownloadDecision(remote);
        }

        // ---- Delta (RECON-02) ----

        [Test]
        public void SynthesizeFromDecisions_backfills_full_whole_range_minus_existing()
        {
            // Gateway whole {0,1,2,4,5,12}; existing {1,2} => backfill {0,3,4,5,6,7,8,9,10,11,12}.
            // Metadata vouches for 12 chapters, so the whole [1..12] range is trusted and the
            // density floor never trims it (the sparse 4/5/12 gaps fall inside the baseline).
            _searched.TotalChapterCount = 12;

            Mocker.GetMock<IChapterService>()
                .Setup(s => s.GetChaptersByManga(_searched.Id))
                .Returns(new List<Chapter>
                {
                    new() { MangaId = 2, ChapterNumber = 1m },
                    new() { MangaId = 2, ChapterNumber = 2m },
                });

            var decisions = new[] { 0m, 1m, 2m, 4m, 5m, 12m }
                .Select(n => Decision("The Forgotten Field", new[] { n }))
                .ToList();

            IList<Chapter> captured = null;
            Mocker.GetMock<IChapterListService>()
                .Setup(s => s.SyncChapters(It.IsAny<Manga.Manga>(), It.IsAny<IEnumerable<Chapter>>(), It.IsAny<bool>()))
                .Callback<Manga.Manga, IEnumerable<Chapter>, bool>((_, list, _) => captured = list?.ToList());

            Subject.SynthesizeFromDecisions(_searched, decisions);

            captured.Should().NotBeNull();
            captured.Select(c => c.ChapterNumber).Should().BeEquivalentTo(
                new[] { 0m, 3m, 4m, 5m, 6m, 7m, 8m, 9m, 10m, 11m, 12m });
        }

        [Test]
        public void SynthesizeFromDecisions_never_synthesizes_fractional_on_search()
        {
            // Whole {1,2} plus a 14.5 release => the fractional must NOT appear and must NOT
            // inflate maxWhole. Existing none => backfill {1,2}; chapter 0 is NOT synthesized
            // because no chapter-0 release was attributed.
            var decisions = new List<MangaDownloadDecision>
            {
                Decision("The Forgotten Field", new[] { 1m }),
                Decision("The Forgotten Field", new[] { 2m }),
                Decision("The Forgotten Field", new[] { 14.5m }),
            };

            IList<Chapter> captured = null;
            Mocker.GetMock<IChapterListService>()
                .Setup(s => s.SyncChapters(It.IsAny<Manga.Manga>(), It.IsAny<IEnumerable<Chapter>>(), It.IsAny<bool>()))
                .Callback<Manga.Manga, IEnumerable<Chapter>, bool>((_, list, _) => captured = list?.ToList());

            Subject.SynthesizeFromDecisions(_searched, decisions);

            captured.Should().NotBeNull();
            captured.Select(c => c.ChapterNumber).Should().BeEquivalentTo(new[] { 1m, 2m });
            captured.Should().NotContain(c => c.ChapterNumber == 14.5m);
        }

        [Test]
        public void SynthesizeFromDecisions_does_not_synthesize_phantom_chapter_zero()
        {
            // A single max-whole attributed release (chapter 10), existing none => backfill the
            // contiguous range {1..10}. Chapter 0 must NOT appear: no chapter-0 release was
            // attributed, so synthesizing a phantom Chapter 0 is a bug. Metadata vouches for 10
            // chapters, so the [1..10] range is trusted and the density floor leaves it intact.
            _searched.TotalChapterCount = 10;

            var decisions = new List<MangaDownloadDecision>
            {
                Decision("The Forgotten Field", new[] { 10m }),
            };

            IList<Chapter> captured = null;
            Mocker.GetMock<IChapterListService>()
                .Setup(s => s.SyncChapters(It.IsAny<Manga.Manga>(), It.IsAny<IEnumerable<Chapter>>(), It.IsAny<bool>()))
                .Callback<Manga.Manga, IEnumerable<Chapter>, bool>((_, list, _) => captured = list?.ToList());

            Subject.SynthesizeFromDecisions(_searched, decisions);

            captured.Should().NotBeNull();
            captured.Select(c => c.ChapterNumber).Should().BeEquivalentTo(
                new[] { 1m, 2m, 3m, 4m, 5m, 6m, 7m, 8m, 9m, 10m });
            captured.Should().NotContain(c => c.ChapterNumber == 0m);
        }

        [Test]
        public void SynthesizeFromDecisions_synthesizes_chapter_zero_when_zero_release_attributed()
        {
            // A genuine chapter-0 release IS attributed (gateway {0,3}, existing none) => Chapter 0
            // is synthesized alongside the {1,2,3} backfill. Metadata vouches for 3 chapters, so
            // the trusted [1..3] range fills regardless of the (sparse) gateway sample.
            _searched.TotalChapterCount = 3;

            var decisions = new List<MangaDownloadDecision>
            {
                Decision("The Forgotten Field", new[] { 0m }),
                Decision("The Forgotten Field", new[] { 3m }),
            };

            IList<Chapter> captured = null;
            Mocker.GetMock<IChapterListService>()
                .Setup(s => s.SyncChapters(It.IsAny<Manga.Manga>(), It.IsAny<IEnumerable<Chapter>>(), It.IsAny<bool>()))
                .Callback<Manga.Manga, IEnumerable<Chapter>, bool>((_, list, _) => captured = list?.ToList());

            Subject.SynthesizeFromDecisions(_searched, decisions);

            captured.Should().NotBeNull();
            captured.Select(c => c.ChapterNumber).Should().BeEquivalentTo(new[] { 0m, 1m, 2m, 3m });
        }

        [Test]
        public void SynthesizeFromDecisions_synthesizes_nothing_when_all_attributed_numbers_are_fractional()
        {
            // CodeRabbit PR #328: attribution passes but EVERY number is fractional => there is
            // no whole-number backlog. The old DefaultIfEmpty(0m).Max() forced maxWhole=0 and
            // synthesized a phantom chapter 0. Assert SyncChapters is never reached.
            var decisions = new List<MangaDownloadDecision>
            {
                Decision("The Forgotten Field", new[] { 1.5m }),
                Decision("The Forgotten Field", new[] { 2.5m }),
            };

            IList<Chapter> captured = null;
            Mocker.GetMock<IChapterListService>()
                .Setup(s => s.SyncChapters(It.IsAny<Manga.Manga>(), It.IsAny<IEnumerable<Chapter>>(), It.IsAny<bool>()))
                .Callback<Manga.Manga, IEnumerable<Chapter>, bool>((_, list, _) => captured = list?.ToList());

            Subject.SynthesizeFromDecisions(_searched, decisions);

            captured.Should().BeNull("no whole-number backlog exists, so no phantom chapter 0 is synthesized");
            Mocker.GetMock<IChapterListService>()
                .Verify(s => s.SyncChapters(It.IsAny<Manga.Manga>(), It.IsAny<IEnumerable<Chapter>>(), It.IsAny<bool>()),
                    Times.Never);
        }

        // ---- Attribution (RECON-02 / D-06/D-07) ----

        [Test]
        public void SynthesizeFromDecisions_does_not_synthesize_for_unrelated_release()
        {
            // "Killing Field" — neither ID-match nor exact-title match to "The Forgotten Field".
            Mocker.GetMock<IMangaService>()
                .Setup(s => s.FindByTitle(It.IsAny<string>()))
                .Returns((Manga.Manga)null);
            Mocker.GetMock<IMangaService>()
                .Setup(s => s.FindByAlternativeTitle(It.IsAny<string>()))
                .Returns((Manga.Manga)null);

            var decisions = new List<MangaDownloadDecision>
            {
                Decision("Killing Field", new[] { 1m }),
                Decision("Killing Field", new[] { 2m }),
                Decision("Killing Field", new[] { 3m }),
            };

            Subject.SynthesizeFromDecisions(_searched, decisions);

            // Zero rows synthesized => SyncChapters never called (or called with empty).
            Mocker.GetMock<IChapterListService>()
                .Verify(
                    s => s.SyncChapters(It.IsAny<Manga.Manga>(),
                        It.Is<IEnumerable<Chapter>>(list => list.Any()),
                        It.IsAny<bool>()),
                    Times.Never);
        }

        [Test]
        public void SynthesizeFromDecisions_attributes_release_by_id_match_with_fuzzy_title()
        {
            // Fuzzy title that resolves to NOTHING by title, but Ids.mangadexId matches the
            // searched manga => must be attributed and backfilled.
            Mocker.GetMock<IMangaService>()
                .Setup(s => s.FindByTitle(It.IsAny<string>()))
                .Returns((Manga.Manga)null);
            Mocker.GetMock<IMangaService>()
                .Setup(s => s.FindByAlternativeTitle(It.IsAny<string>()))
                .Returns((Manga.Manga)null);

            var ids = new Dictionary<string, object>
            {
                { "mangadexId", "fb716acf-4257-4613-bf30-32d7c32dc199" },
            };
            var decisions = new List<MangaDownloadDecision>
            {
                Decision("4gott3n F1eld (fuzzy)", new[] { 0m }, ids),
                Decision("4gott3n F1eld (fuzzy)", new[] { 1m }, ids),
            };

            IList<Chapter> captured = null;
            Mocker.GetMock<IChapterListService>()
                .Setup(s => s.SyncChapters(It.IsAny<Manga.Manga>(), It.IsAny<IEnumerable<Chapter>>(), It.IsAny<bool>()))
                .Callback<Manga.Manga, IEnumerable<Chapter>, bool>((_, list, _) => captured = list?.ToList());

            Subject.SynthesizeFromDecisions(_searched, decisions);

            captured.Should().NotBeNull();
            captured.Select(c => c.ChapterNumber).Should().BeEquivalentTo(new[] { 0m, 1m });
        }

        // ---- Monitor policy (RECON-03 / D-05) ----

        [Test]
        public void SynthesizeFromDecisions_marks_rows_unmonitored_when_MonitorNewItems_None()
        {
            _searched.MonitorNewItems = MangaMonitorNewItems.None;

            var decisions = new List<MangaDownloadDecision>
            {
                Decision("The Forgotten Field", new[] { 0m }),
                Decision("The Forgotten Field", new[] { 1m }),
            };

            IList<Chapter> captured = null;
            Mocker.GetMock<IChapterListService>()
                .Setup(s => s.SyncChapters(It.IsAny<Manga.Manga>(), It.IsAny<IEnumerable<Chapter>>(), It.IsAny<bool>()))
                .Callback<Manga.Manga, IEnumerable<Chapter>, bool>((_, list, _) => captured = list?.ToList());

            Subject.SynthesizeFromDecisions(_searched, decisions);

            captured.Should().NotBeNull();
            captured.Should().OnlyContain(c => c.Monitored == false);
        }

        [Test]
        public void SynthesizeFromDecisions_marks_rows_monitored_when_MonitorNewItems_All()
        {
            _searched.MonitorNewItems = MangaMonitorNewItems.All;

            var decisions = new List<MangaDownloadDecision>
            {
                Decision("The Forgotten Field", new[] { 0m }),
                Decision("The Forgotten Field", new[] { 1m }),
            };

            IList<Chapter> captured = null;
            Mocker.GetMock<IChapterListService>()
                .Setup(s => s.SyncChapters(It.IsAny<Manga.Manga>(), It.IsAny<IEnumerable<Chapter>>(), It.IsAny<bool>()))
                .Callback<Manga.Manga, IEnumerable<Chapter>, bool>((_, list, _) => captured = list?.ToList());

            Subject.SynthesizeFromDecisions(_searched, decisions);

            captured.Should().NotBeNull();
            captured.Should().OnlyContain(c => c.Monitored == true);
        }

        [Test]
        public void SynthesizeFromDecisions_default_enum_value_is_monitored()
        {
            // default(MangaMonitorNewItems) == 0 == All => Monitored=true.
            _searched.MonitorNewItems = default;

            var decisions = new List<MangaDownloadDecision>
            {
                Decision("The Forgotten Field", new[] { 0m }),
            };

            IList<Chapter> captured = null;
            Mocker.GetMock<IChapterListService>()
                .Setup(s => s.SyncChapters(It.IsAny<Manga.Manga>(), It.IsAny<IEnumerable<Chapter>>(), It.IsAny<bool>()))
                .Callback<Manga.Manga, IEnumerable<Chapter>, bool>((_, list, _) => captured = list?.ToList());

            Subject.SynthesizeFromDecisions(_searched, decisions);

            captured.Should().NotBeNull();
            captured.Should().OnlyContain(c => c.Monitored == true);
        }

        // ---- No-clobber (RECON-04 / D-03) ----

        [Test]
        public void SynthesizeFromDecisions_never_passes_already_cataloged_numbers()
        {
            // Existing {0,1,2}; gateway exposes {0,1,2,3}. Only {3} (the genuinely-absent
            // number) may reach SyncChapters — passing 0/1/2 would let the verbatim Title
            // assignment in ChapterListService clobber real MangaDex titles with null.
            Mocker.GetMock<IChapterService>()
                .Setup(s => s.GetChaptersByManga(_searched.Id))
                .Returns(new List<Chapter>
                {
                    new() { MangaId = 2, ChapterNumber = 0m, Title = "Real 0" },
                    new() { MangaId = 2, ChapterNumber = 1m, Title = "Real 1" },
                    new() { MangaId = 2, ChapterNumber = 2m, Title = "Real 2" },
                });

            var decisions = new[] { 0m, 1m, 2m, 3m }
                .Select(n => Decision("The Forgotten Field", new[] { n }))
                .ToList();

            IList<Chapter> captured = null;
            Mocker.GetMock<IChapterListService>()
                .Setup(s => s.SyncChapters(It.IsAny<Manga.Manga>(), It.IsAny<IEnumerable<Chapter>>(), It.IsAny<bool>()))
                .Callback<Manga.Manga, IEnumerable<Chapter>, bool>((_, list, _) => captured = list?.ToList());

            Subject.SynthesizeFromDecisions(_searched, decisions);

            captured.Should().NotBeNull();
            captured.Select(c => c.ChapterNumber).Should().BeEquivalentTo(new[] { 3m });
            captured.Should().NotContain(c => c.ChapterNumber == 0m || c.ChapterNumber == 1m || c.ChapterNumber == 2m);
        }

        // ---- Density-floor stray cut (manga-removed-from-metadata-source follow-up) ----

        [Test]
        public void SynthesizeFromDecisions_drops_sparse_stray_numbers_far_above_metadata_count()
        {
            // The Heavenly Demon Wants a Quiet Life: metadata count 86, catalog already holds
            // 1..86, and mangaball reports a handful of MISLABELED stray releases far above the
            // real count. These must NOT drag the synthesized range up to 726 and spawn ~640
            // phantom "Missing" chapters. Each stray is a lone number, so its post-baseline
            // density is ~1/(stray-86) << 0.5 and the cut falls back to the trusted baseline.
            _searched.TotalChapterCount = 86;

            Mocker.GetMock<IChapterService>()
                .Setup(s => s.GetChaptersByManga(_searched.Id))
                .Returns(Enumerable.Range(1, 86)
                    .Select(n => new Chapter { MangaId = 2, ChapterNumber = n })
                    .ToList());

            var decisions = new[] { 84m, 85m, 86m, 164m, 592m, 694m, 703m, 712m, 726m }
                .Select(n => Decision("The Heavenly Demon Wants a Quiet Life", new[] { n }))
                .ToList();

            // Attribution resolves these to the searched manga.
            Mocker.GetMock<IMangaService>()
                .Setup(s => s.FindByTitle(It.IsAny<string>()))
                .Returns(_searched);

            Subject.SynthesizeFromDecisions(_searched, decisions);

            // Nothing genuinely-absent beyond the trusted [1..86] => no rows reach SyncChapters.
            Mocker.GetMock<IChapterListService>()
                .Verify(
                    s => s.SyncChapters(It.IsAny<Manga.Manga>(),
                        It.Is<IEnumerable<Chapter>>(list => list.Any()),
                        It.IsAny<bool>()),
                    Times.Never);
        }

        [Test]
        public void SynthesizeFromDecisions_keeps_contiguous_extension_but_drops_strays()
        {
            // Metadata count 86, catalog 1..86. The gateway legitimately runs a few chapters
            // AHEAD of metadata (87..90 — a dense contiguous extension) AND carries two strays
            // (164, 592). The contiguous run clears the floor (4/4 = 1.0 at top 90); the strays
            // do not. Result: 87..90 are synthesized, the strays are dropped.
            _searched.TotalChapterCount = 86;

            Mocker.GetMock<IChapterService>()
                .Setup(s => s.GetChaptersByManga(_searched.Id))
                .Returns(Enumerable.Range(1, 86)
                    .Select(n => new Chapter { MangaId = 2, ChapterNumber = n })
                    .ToList());

            var decisions = new[] { 87m, 88m, 89m, 90m, 164m, 592m }
                .Select(n => Decision("The Forgotten Field", new[] { n }))
                .ToList();

            IList<Chapter> captured = null;
            Mocker.GetMock<IChapterListService>()
                .Setup(s => s.SyncChapters(It.IsAny<Manga.Manga>(), It.IsAny<IEnumerable<Chapter>>(), It.IsAny<bool>()))
                .Callback<Manga.Manga, IEnumerable<Chapter>, bool>((_, list, _) => captured = list?.ToList());

            Subject.SynthesizeFromDecisions(_searched, decisions);

            captured.Should().NotBeNull();
            captured.Select(c => c.ChapterNumber).Should().BeEquivalentTo(new[] { 87m, 88m, 89m, 90m });
            captured.Should().NotContain(c => c.ChapterNumber == 164m || c.ChapterNumber == 592m);
        }

        [Test]
        public void SynthesizeFromDecisions_fills_when_metadata_undercounts_and_gateway_is_contiguous()
        {
            // The legit "metadata says 10, reality is 100" case the user explicitly wants kept:
            // metadata undercounts (10), catalog 1..10, gateway exposes a CONTIGUOUS 11..20 run.
            // Post-baseline density at top 20 is 10/10 = 1.0, so the full extension is honored.
            _searched.TotalChapterCount = 10;

            Mocker.GetMock<IChapterService>()
                .Setup(s => s.GetChaptersByManga(_searched.Id))
                .Returns(Enumerable.Range(1, 10)
                    .Select(n => new Chapter { MangaId = 2, ChapterNumber = n })
                    .ToList());

            var decisions = Enumerable.Range(11, 10)   // 11..20
                .Select(n => Decision("The Forgotten Field", new[] { (decimal)n }))
                .ToList();

            IList<Chapter> captured = null;
            Mocker.GetMock<IChapterListService>()
                .Setup(s => s.SyncChapters(It.IsAny<Manga.Manga>(), It.IsAny<IEnumerable<Chapter>>(), It.IsAny<bool>()))
                .Callback<Manga.Manga, IEnumerable<Chapter>, bool>((_, list, _) => captured = list?.ToList());

            Subject.SynthesizeFromDecisions(_searched, decisions);

            captured.Should().NotBeNull();
            captured.Select(c => c.ChapterNumber).Should().BeEquivalentTo(
                Enumerable.Range(11, 10).Select(n => (decimal)n));
        }

        // ---- On-grab (RECON-04 / D-04) ----

        [Test]
        public void SynthesizeForGrab_synthesizes_single_absent_whole_number()
        {
            Mocker.GetMock<IChapterService>()
                .Setup(s => s.FindByMangaAndNumber(_searched.Id, 7m))
                .Returns((Chapter)null);

            var remote = new RemoteChapter
            {
                Manga = _searched,
                ParsedChapterInfo = new ParsedChapterInfo
                {
                    MangaTitle = "The Forgotten Field",
                    ChapterNumbers = new[] { 7m },
                    ChapterType = ChapterType.Regular,
                },
                Release = new ReleaseInfo { Title = "The Forgotten Field" },
            };

            IList<Chapter> captured = null;
            Mocker.GetMock<IChapterListService>()
                .Setup(s => s.SyncChapters(It.IsAny<Manga.Manga>(), It.IsAny<IEnumerable<Chapter>>(), It.IsAny<bool>()))
                .Callback<Manga.Manga, IEnumerable<Chapter>, bool>((_, list, _) => captured = list?.ToList());

            Subject.SynthesizeForGrab(remote);

            captured.Should().NotBeNull();
            captured.Should().HaveCount(1);
            captured[0].ChapterNumber.Should().Be(7m);
        }

        [Test]
        public void SynthesizeForGrab_synthesizes_single_absent_fractional_number()
        {
            Mocker.GetMock<IChapterService>()
                .Setup(s => s.FindByMangaAndNumber(_searched.Id, 14.5m))
                .Returns((Chapter)null);

            var remote = new RemoteChapter
            {
                Manga = _searched,
                ParsedChapterInfo = new ParsedChapterInfo
                {
                    MangaTitle = "The Forgotten Field",
                    ChapterNumbers = new[] { 14.5m },
                    ChapterType = ChapterType.Regular,
                },
                Release = new ReleaseInfo { Title = "The Forgotten Field" },
            };

            IList<Chapter> captured = null;
            Mocker.GetMock<IChapterListService>()
                .Setup(s => s.SyncChapters(It.IsAny<Manga.Manga>(), It.IsAny<IEnumerable<Chapter>>(), It.IsAny<bool>()))
                .Callback<Manga.Manga, IEnumerable<Chapter>, bool>((_, list, _) => captured = list?.ToList());

            Subject.SynthesizeForGrab(remote);

            captured.Should().NotBeNull();
            captured.Should().HaveCount(1);
            captured[0].ChapterNumber.Should().Be(14.5m);
        }

        [Test]
        public void SynthesizeForGrab_noops_when_chapter_already_exists()
        {
            Mocker.GetMock<IChapterService>()
                .Setup(s => s.FindByMangaAndNumber(_searched.Id, 3m))
                .Returns(new Chapter { Id = 99, MangaId = 2, ChapterNumber = 3m, Title = "Real 3" });

            var remote = new RemoteChapter
            {
                Manga = _searched,
                ParsedChapterInfo = new ParsedChapterInfo
                {
                    MangaTitle = "The Forgotten Field",
                    ChapterNumbers = new[] { 3m },
                    ChapterType = ChapterType.Regular,
                },
                Release = new ReleaseInfo { Title = "The Forgotten Field" },
            };

            Subject.SynthesizeForGrab(remote);

            Mocker.GetMock<IChapterListService>()
                .Verify(s => s.SyncChapters(It.IsAny<Manga.Manga>(), It.IsAny<IEnumerable<Chapter>>(), It.IsAny<bool>()),
                    Times.Never);
        }

        // WR-01 + WR-02: SynthesizeForGrab returns the resolved row (re-read post-sync so it
        // carries its persisted Id) for the caller to re-hydrate RemoteChapter.Chapters, AND
        // it calls SyncChapters with preserveExistingOnNull: true so a synthesized null Title
        // can never clobber a real title written by a concurrent refresh.
        [Test]
        public void SynthesizeForGrab_returns_rehydration_rows_and_preserves_existing_on_null()
        {
            var saved = new Chapter { Id = 555, MangaId = _searched.Id, ChapterNumber = 7m };
            Mocker.GetMock<IChapterService>()
                .SetupSequence(s => s.FindByMangaAndNumber(_searched.Id, 7m))
                .Returns((Chapter)null)   // absence check -> synthesize
                .Returns(saved);          // post-sync re-read -> resolved with Id

            var remote = new RemoteChapter
            {
                Manga = _searched,
                ParsedChapterInfo = new ParsedChapterInfo
                {
                    MangaTitle = "The Forgotten Field",
                    ChapterNumbers = new[] { 7m },
                    ChapterType = ChapterType.Regular,
                },
                Release = new ReleaseInfo { Title = "The Forgotten Field" },
            };

            var result = Subject.SynthesizeForGrab(remote);

            result.Should().ContainSingle(c => c.Id == 555);

            Mocker.GetMock<IChapterListService>()
                .Verify(s => s.SyncChapters(It.IsAny<Manga.Manga>(), It.IsAny<IEnumerable<Chapter>>(), true),
                    Times.Once);
        }
    }
}
