using System.Collections.Generic;
using System.Linq;
using NLog;

namespace NzbDrone.Core.Manga
{
    // Phase 16.1 Wave 3 (REVERT-03): single-pass SyncChapters reverted from the Phase 16
    // two-method split. Mirrors Sonarr's RefreshEpisodeService.RefreshEpisodeInfo upsert
    // pattern at the canonical (MangaId, ChapterNumber) grain. Per-translation language
    // and scanlation-group axes live on ChapterFile after import (Phase 6 PIPELINE-04:
    // ChapterFile.TranslatedLanguage + ChapterFile.ScanlationGroup), NOT on a sibling
    // persistent layer.
    //
    // Idempotency:
    //   (a) Upsert is keyed on (MangaId, ChapterNumber) — UNIQUE index from Plan 16-02
    //       enforces. Re-running RefreshMangaCommand produces identical Chapter state.
    //   (b) Inputs are dedup'd on ChapterNumber via DistinctBy BEFORE the upsert pass —
    //       BL-05 multi-translation regression safety net (mirror of Sonarr's
    //       DistinctBy(new { SeasonNumber, EpisodeNumber }) precedent).
    //   (c) Failure mode: any natural-key violation fails LOUD via UNIQUE-violation thrown
    //       at INSERT time.
    //
    // STALE-HANDLING DECISION (LOCKED per Phase 16.1 CONTEXT.md additional_context Pitfall #4
    // + PATTERNS.md Pitfall 6): NO DELETE of stale chapters. Pre-Phase-16 behavior retained;
    // Sonarr's RefreshEpisodeService DOES delete stale, but SPEC scope is "revert," not
    // "Sonarr-canonicalize stale-handling." Defer to a future phase if needed.
    public sealed class ChapterListService : IChapterListService
    {
        private readonly IChapterRepository _chapterRepo;
        private readonly Logger _logger;

        public ChapterListService(IChapterRepository chapterRepo, Logger logger)
        {
            _chapterRepo = chapterRepo;
            _logger = logger;
        }

        public void SyncChapters(Manga manga, IEnumerable<Chapter> remoteChapters, bool preserveExistingOnNull = false)
        {
            if (remoteChapters == null)
            {
                return;
            }

            // BL-05 multi-translation dedup safety net: collapse duplicate remote entries on
            // the canonical natural key BEFORE the upsert. Mirror of Sonarr's
            // DistinctBy(new { SeasonNumber, EpisodeNumber }) precedent.
            var dupeFree = remoteChapters
                .DistinctBy(c => c.ChapterNumber)
                .ToList();

            if (dupeFree.Count == 0)
            {
                return;
            }

            var existing = _chapterRepo.GetByMangaId(manga.Id);
            var existingByNumber = existing.ToDictionary(c => c.ChapterNumber);

            var toInsert = new List<Chapter>();
            var toUpdate = new List<Chapter>();

            foreach (var remote in dupeFree)
            {
                if (existingByNumber.TryGetValue(remote.ChapterNumber, out var match))
                {
                    // Update-in-place: copy mutable fields with null-coalesce so a missing
                    // input field does NOT clobber a previously-set canonical value.
                    // Monitored is intentionally NOT touched on update — Sonarr precedent:
                    // Monitored is set on insert, then user-controlled afterwards.
                    //
                    // EXCEPTION: Title is assigned verbatim (no null-coalesce). The metadata
                    // source defines null as the canonical "no English title available" value
                    // (manga-details-chapter-titles fix, 2026-05-10 — MangaDexMetadataSource.MapChapter
                    // suppresses non-EN entries' Title slot so scanlator-group commentary
                    // does not pollute Chapter.Title). Coalescing here would preserve stale
                    // polluted titles from pre-fix ingestions — exactly the bug we are
                    // shipping the fix for. Title is intentionally treated as fully
                    // remote-driven; the structural mutable fields below keep their
                    // coalesce guard because their semantic is "transient API gap" rather
                    // than "canonically absent."
                    //
                    // Phase 40 WR-02/IN-02: when preserveExistingOnNull is set (the synthesis
                    // caller), the verbatim semantics flip for Title/ChapterType — a synthesized
                    // null is "unknown," NOT canonical, so it must not clobber a real value a
                    // concurrent refresh wrote in the TOCTOU window. The metadata-refresh caller
                    // (default false) keeps the canonical-null verbatim behavior unchanged.
                    if (preserveExistingOnNull)
                    {
                        match.Title = remote.Title ?? match.Title;
                    }
                    else
                    {
                        match.Title = remote.Title;
                    }

                    match.AbsoluteChapterNumber = remote.AbsoluteChapterNumber ?? match.AbsoluteChapterNumber;
                    match.VolumeNumber = remote.VolumeNumber ?? match.VolumeNumber;

                    // ChapterType is a non-nullable enum, so it has no null sentinel; under
                    // preserveExistingOnNull the synthesized row carries no authoritative type
                    // (it defaults to Regular), so leave the existing canonical type untouched.
                    if (!preserveExistingOnNull)
                    {
                        match.ChapterType = remote.ChapterType;
                    }

                    match.FirstReleaseDate = remote.FirstReleaseDate ?? match.FirstReleaseDate;
                    match.ExternalId = remote.ExternalId ?? match.ExternalId;
                    toUpdate.Add(match);
                }
                else
                {
                    remote.MangaId = manga.Id;

                    // Monitored = true comes from MangaDexMetadataSource.MapChapter (Sonarr
                    // precedent: new chapters monitored by default).
                    toInsert.Add(remote);
                }
            }

            if (toInsert.Count > 0)
            {
                _chapterRepo.InsertMany(toInsert);
            }

            if (toUpdate.Count > 0)
            {
                _chapterRepo.UpdateMany(toUpdate);
            }

            // NO DELETE branch — locked stale-handling decision (see file header).
        }
    }
}
