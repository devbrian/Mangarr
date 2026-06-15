using System;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Manga.Model;
using NzbDrone.Core.Profiles.CustomFormats;
using NzbDrone.Core.Profiles.Translations;

namespace NzbDrone.Core.DecisionEngine.Manga.Specifications
{
    // Sonarr divergence: NEW disk-aware reject spec per debug session
    // `rss-regrab-existing-chapter` (2026-06-15) — see DIVERGENCE.md.
    // Role-match analog: TV CutoffSpecification + UpgradeDiskSpecification at
    // src/NzbDrone.Core/DecisionEngine/Specifications/{Cutoff,UpgradeDisk}Specification.cs.
    //
    // WHY THIS EXISTS: when Mangarr dropped Sonarr's quality model (Phase 5 D-04) it also
    // dropped the disk-aware reject chain that, on the TV side, rejects an RSS release for an
    // episode you ALREADY HAVE a (cutoff-met / non-upgrade) file for. No manga peer was ported,
    // so the manga RSS decision pipeline never inspected the existing ChapterFile before
    // grabbing. The only already-have guard, AlreadyImportedChapterSpecification, is a narrow
    // per-download-session guard that requires an Imported(3) ChapterHistory row paired by
    // DownloadId to the latest Grabbed row — a pairing that is structurally impossible for a
    // chapter imported under an empty DownloadId whose every re-import records Ignored(5), never
    // a fresh Imported(3). The result was an already-imported chapter re-grabbed on EVERY RSS
    // sync cycle (gateway re-delivers → import skips file-exists → repeat forever).
    //
    // This spec closes the gap by mirroring the import-side gate
    // (MediaFiles/MangaImport/Specifications/UpgradeSpecification.cs) on the DECISION side, so the
    // decision pipeline rejects a non-upgrade BEFORE grabbing — keeping decision and import in
    // agreement (the decision must not grab what import will only skip). The comparison is the
    // SAME D-10 three-state gate + D-08 language-rank → CF-score ordering used at import time.
    //
    // Mirrors Sonarr UpgradeDiskSpecification's "reject the whole release if ANY mapped chapter
    // already has a file the candidate doesn't beat" semantics. For the common single-chapter
    // manga release this is exactly "chapter has a satisfactory file → reject".
    //
    // Gap-1 invariant preserved (mirrors AlreadyImportedChapterSpecification): a chapter whose
    // ChapterFile was deleted (ChapterFileId == 0) has nothing to be "already have" against, so it
    // is SKIPPED and the release stays grabbable — delete→redownload still works.
    //
    // Priority = Disk (does ChapterFile DB I/O — runs after Database/Default specs, mirrors TV
    // UpgradeDiskSpecification). Type = Permanent. Implements IMangaDecisionEngineSpecification
    // ONLY (Pitfall 6 guard preserved).
    // Phase 8 cleanup: collapse with TV UpgradeDiskSpecification + CutoffSpecification when Tv/ deletes.
    public class UpgradeDiskSpecification : IMangaDecisionEngineSpecification
    {
        private readonly IChapterFileService _chapterFileService;
        private readonly ITranslationProfileService _translationProfileService;
        private readonly ICustomFormatProfileService _customFormatProfileService;
        private readonly ICustomFormatCalculationService _formatCalculator;
        private readonly IDiskProvider _diskProvider;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public UpgradeDiskSpecification(IChapterFileService chapterFileService,
                                        ITranslationProfileService translationProfileService,
                                        ICustomFormatProfileService customFormatProfileService,
                                        ICustomFormatCalculationService formatCalculator,
                                        IDiskProvider diskProvider,
                                        IConfigService configService,
                                        Logger logger)
        {
            _chapterFileService = chapterFileService;
            _translationProfileService = translationProfileService;
            _customFormatProfileService = customFormatProfileService;
            _formatCalculator = formatCalculator;
            _diskProvider = diskProvider;
            _configService = configService;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Disk;
        public RejectionType Type => RejectionType.Permanent;

        public DownloadSpecDecision IsSatisfiedBy(RemoteChapter subject, ReleaseDecisionInformation information)
        {
            if (subject?.Manga == null)
            {
                return DownloadSpecDecision.Accept();
            }

            var translationProfile = ResolveTranslationProfile(subject);
            var customFormatProfile = ResolveCustomFormatProfile(subject);

            foreach (var chapter in subject.Chapters)
            {
                // Gap-1 invariant (mirrors AlreadyImportedChapterSpecification): a chapter with no
                // current ChapterFile — e.g. the file was deleted to force a redownload — has
                // nothing to be "already have" against. Without this guard a stale row would
                // permanently block re-grabbing the chapter.
                if (chapter.ChapterFileId.GetValueOrDefault() == 0)
                {
                    continue;
                }

                var existingFiles = _chapterFileService.GetFilesByChapter(chapter.Id);
                if (existingFiles == null || existingFiles.Count == 0)
                {
                    // ChapterFileId set but no file rows resolved (orphaned FK) — nothing to
                    // compare against; let the release through rather than block on stale state.
                    continue;
                }

                // P2 (PR #372 review): defer ChapterFile rows whose artifact is MISSING from disk
                // to DeletedChapterFileSpecification — that spec runs in this same Disk priority
                // bucket and emits a TEMPORARY ChapterNotMonitored rejection so the deleted-file
                // flow can reconcile (disk-scan unmonitor) or re-download. The decision maker
                // evaluates every spec in a bucket before stopping and MangaDownloadDecision is
                // TemporarilyRejected only if ALL rejections are Temporary — so a PERMANENT
                // DiskUpgradesNotAllowed/DiskNotUpgrade here for a missing file would poison that
                // temporary path. A missing on-disk file is also a legitimate re-download trigger,
                // so it must never become a permanent reject. Compare only files present on disk.
                var presentFiles = existingFiles.Where(f => !IsChapterFileMissing(subject.Manga, f)).ToList();
                if (presentFiles.Count == 0)
                {
                    continue;
                }

                // D-10 three-state fallback: per-Manga override wins; otherwise AND-merge per-profile flags.
                var effectiveUpgradeAllowed = subject.Manga.UpgradeAllowedOverride
                    ?? ((translationProfile?.UpgradeAllowed ?? true) && (customFormatProfile?.UpgradeAllowed ?? false));

                if (!effectiveUpgradeAllowed)
                {
                    _logger.Debug(
                        "Chapter {0} already has a file and upgrades are not allowed (manga.Override={1}, translation.UpgradeAllowed={2}, cf.UpgradeAllowed={3}); rejecting",
                        chapter.Id,
                        subject.Manga.UpgradeAllowedOverride,
                        translationProfile?.UpgradeAllowed,
                        customFormatProfile?.UpgradeAllowed);

                    return DownloadSpecDecision.Reject(
                        DownloadRejectionReason.DiskUpgradesNotAllowed,
                        "Existing chapter file present and upgrades not allowed by profile/manga override");
                }

                // Compare the incoming candidate vs each existing on-disk ChapterFile. If any
                // existing file is at-least-as-good (better language rank, or same rank with
                // same-or-better CF score), the candidate is not an upgrade — reject (mirrors
                // import-side UpgradeSpecification and Sonarr UpgradeDiskSpecification's
                // whole-release reject).
                foreach (var existingFile in presentFiles)
                {
                    var rankCompare = CompareLanguageRank(existingFile.TranslatedLanguage, subject.Release?.TranslatedLanguage, translationProfile);

                    if (rankCompare < 0)
                    {
                        // Existing has a LOWER (better) rank — incoming is a downgrade.
                        _logger.Debug(
                            "Existing chapter file {0} is a better language match than the candidate; rejecting",
                            existingFile.Path);
                        return DownloadSpecDecision.Reject(
                            DownloadRejectionReason.DiskNotUpgrade,
                            "Existing chapter file is a better language match (existing={0}, release={1})",
                            existingFile.TranslatedLanguage ?? "(none)",
                            subject.Release?.TranslatedLanguage ?? "(none)");
                    }

                    if (rankCompare == 0)
                    {
                        // Same language rank — fall through to CF score comparison.
                        var existingScore = ComputeExistingFileCustomFormatScore(existingFile, subject, customFormatProfile);
                        var incomingScore = subject.CustomFormatScore;

                        if (incomingScore <= existingScore)
                        {
                            _logger.Debug(
                                "Candidate CF score ({0}) does not improve on existing chapter file ({1}) at same language rank; rejecting",
                                incomingScore,
                                existingScore);
                            return DownloadSpecDecision.Reject(
                                DownloadRejectionReason.DiskNotUpgrade,
                                "Existing chapter file is at least as good (same language; existing CF score {0} >= release {1})",
                                existingScore,
                                incomingScore);
                        }
                    }

                    // rankCompare > 0 — incoming has BETTER (lower-index) rank. This existing file
                    // is beaten; keep checking the other existing files for this chapter.
                }
            }

            return DownloadSpecDecision.Accept();
        }

        // Returns: -1 if existing has lower (better) rank index; 0 if equal; +1 if existing has worse rank.
        // Mirrors UpgradeSpecification.CompareLanguageRank / MangaDownloadDecisionComparer semantics.
        // Unranked = int.MaxValue.
        private int CompareLanguageRank(string existingLanguage, string incomingLanguage, TranslationProfile profile)
        {
            var existingRank = ResolveRank(existingLanguage, profile);
            var incomingRank = ResolveRank(incomingLanguage, profile);
            return existingRank.CompareTo(incomingRank);
        }

        // Mirrors DeletedChapterFileSpecification.IsChapterFileMissing — a ChapterFile row whose
        // artifact is gone from disk is not an "existing file" for upgrade purposes.
        //
        // Path resolution mirrors the deleted-file spec (Combine(manga.Path, RelativePath)) with a
        // fallback to the absolute ChapterFile.Path for legacy/partially-populated rows that lack a
        // RelativePath. When NEITHER resolves to a usable path we treat the row as MISSING (true) so
        // it is skipped rather than converted into a permanent reject — an unconfirmable on-disk
        // state must never block deleted-file reconciliation or a legitimate re-download.
        private bool IsChapterFileMissing(NzbDrone.Core.Manga.Manga manga, ChapterFile chapterFile)
        {
            if (manga == null || chapterFile == null)
            {
                return false;
            }

            var fullPath = chapterFile.RelativePath.IsNullOrWhiteSpace()
                ? chapterFile.Path
                : Path.Combine(manga.Path, chapterFile.RelativePath);

            if (fullPath.IsNullOrWhiteSpace())
            {
                return true;
            }

            return !_diskProvider.FileExists(fullPath);
        }

        private static int ResolveRank(string language, TranslationProfile profile)
        {
            if (profile?.Languages == null || language.IsNullOrWhiteSpace())
            {
                return int.MaxValue;
            }

            var index = profile.Languages.FindIndex(l => string.Equals(l, language, StringComparison.OrdinalIgnoreCase));
            return index < 0 ? int.MaxValue : index;
        }

        private TranslationProfile ResolveTranslationProfile(RemoteChapter subject)
        {
            var profileId = subject.Manga?.TranslationProfileId ?? _configService.DefaultTranslationProfileId;
            return ResolveProfile(profileId, id => _translationProfileService.Get(id), "TranslationProfile");
        }

        private CustomFormatProfile ResolveCustomFormatProfile(RemoteChapter subject)
        {
            // WR-08 consistency: prefer the maker-stamped resolved id so this spec scores the
            // existing file against the SAME profile the maker used for the candidate's score.
            var profileId = subject.ResolvedCustomFormatProfileId
                            ?? subject.Manga?.CustomFormatProfileId
                            ?? _configService.DefaultCustomFormatProfileId;
            return ResolveProfile(profileId, id => _customFormatProfileService.Get(id), "CustomFormatProfile");
        }

        // BL-03 (DEF-19-02-01) orphaned/zero-FK guard — mirrors import-side UpgradeSpecification.
        // A Manga can carry a profile id of 0 (the int default the AddManga modal sends when no
        // profile is picked and none is the global default) or an orphaned FK to a since-deleted
        // profile; the profile services' Get throws ModelNotFoundException on a missing/zero row.
        // Degrade to null — the gate null-tolerates both profiles (UpgradeAllowed ?? default,
        // ResolveRank => int.MaxValue), so this means "unranked, no CF scoring".
        private TProfile ResolveProfile<TProfile>(int? profileId, Func<int, TProfile> get, string profileType)
            where TProfile : class
        {
            if (!profileId.HasValue || profileId.Value <= 0)
            {
                return null;
            }

            try
            {
                return get(profileId.Value);
            }
            catch (ModelNotFoundException)
            {
                _logger.Warn("{0} {1} not found (orphaned FK); treating as no profile", profileType, profileId.Value);
                return null;
            }
        }

        // Compute the existing ChapterFile's CF score against the CustomFormatProfile by routing
        // it through the canonical CustomFormatCalculationService.ParseCustomFormat overload that
        // accepts MangaCustomFormatInput. Reconstruct a minimal ReleaseInfo from the ChapterFile's
        // stored provenance (TranslatedLanguage + ScanlationGroup) — the only fields the manga CF
        // specs read (Phase 5 D-09). Mirrors import-side UpgradeSpecification verbatim.
        private int ComputeExistingFileCustomFormatScore(ChapterFile existingFile, RemoteChapter incoming, CustomFormatProfile profile)
        {
            if (profile == null || profile.FormatItems == null || profile.FormatItems.Count == 0)
            {
                return 0;
            }

            var release = new NzbDrone.Core.Parser.Model.ReleaseInfo
            {
                Title = existingFile.GetSceneOrFileName(),
                TranslatedLanguage = existingFile.TranslatedLanguage,
                ScanlationGroup = existingFile.ScanlationGroup
            };

            var input = new MangaCustomFormatInput
            {
                Filename = existingFile.GetSceneOrFileName(),
                Manga = incoming.Manga,
                Release = release,
                ChapterInfo = incoming.ParsedChapterInfo
            };

            var matched = _formatCalculator.ParseCustomFormat(input);
            return profile.CalculateCustomFormatScore(matched);
        }
    }
}
