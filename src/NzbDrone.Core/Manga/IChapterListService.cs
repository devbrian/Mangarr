using System;
using System.Collections.Generic;
using NzbDrone.Core.Parser.Manga;

namespace NzbDrone.Core.Manga
{
    // Sonarr divergence: REWRITTEN per Phase 16 STRUCT-05 — see DIVERGENCE.md.
    // Splits the pre-Phase-16 single-method SyncChapters into two operations:
    //   * EnsureChapter        — upserts canonical (MangaId, ChapterNumber) row (D-04: always creates real row)
    //   * SyncChapterReleases  — upserts per-(language, group) translation rows (D-01: keep stale releases)
    // Mirrors Sonarr's RefreshEpisodeService two-pass shape (canonical row + per-release).
    //
    // Idempotency contract:
    //   (a) EnsureChapter is upsert-on-(MangaId, ChapterNumber) — UNIQUE index forbids dups (Plan 16-02).
    //   (b) SyncChapterReleases is upsert-on-(ChapterId, TranslatedLanguage, ScanlationGroup) — UNIQUE forbids dups.
    //   (c) No DELETE branch in either method — input set never shrinks (D-01 stale retention).
    //   (d) Re-running RefreshMangaCommand produces identical (Chapter, ChapterRelease) state.
    //   (e) Failure mode: any algorithmic regression that violates a natural key fails LOUD via
    //       UNIQUE-violation thrown at INSERT time.
    public interface IChapterListService
    {
        // Per CONTEXT D-04: always creates a real Chapter row. Returns the (existing or new) Chapter.
        // Reads firstReleaseDate from inputs.FirstReleaseDate (per CONTEXT D-02).
        Chapter EnsureChapter(int mangaId, decimal chapterNumber, ChapterEnsureInputs inputs);

        // Per CONTEXT D-01: upsert-on-natural-key, NEVER DELETE missing.
        // Mirrors Sonarr's Episode-row retention — rows are NOT pruned on stale-feed.
        void SyncChapterReleases(int chapterId, IList<ChapterReleaseFeedRow> feedRows);
    }

    // Phase 16 STRUCT-05 record types — feed-row-shape DTOs that flow from the metadata source
    // through RefreshMangaService into ChapterListService. Keep these in this file (alongside
    // the consuming interface) to keep the seam visible at one read.

    public sealed record ChapterEnsureInputs(
        string Title,
        decimal? AbsoluteChapterNumber,
        int? VolumeNumber,
        ChapterType ChapterType,
        DateTime? FirstReleaseDate,
        string ExternalId);

    public sealed record ChapterReleaseFeedRow(
        string TranslatedLanguage,
        string ScanlationGroup,
        DateTime? ReleaseDate,
        string ExternalId);
}
