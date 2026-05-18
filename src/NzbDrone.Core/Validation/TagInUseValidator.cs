using System.Collections.Generic;
using FluentValidation;
using FluentValidation.Results;
using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.Tags;

// Sonarr divergence: Mangarr v1.1 Phase 22 enhancement — Sonarr's TagController DELETE
// relies on TagService.Delete()'s ModelConflictException throw; this validator enables
// FluentValidation 400 + structured error message naming the consuming entities. Sonarr
// v5-develop has NO src/NzbDrone.Core/Validation/TagInUseValidator.cs (verified by
// git cat-file 2026-05-17). DIVERGENCE.md entry recorded at phase close.
namespace NzbDrone.Core.Validation
{
    public interface ITagInUseValidator
    {
        ValidationResult Validate(Tag instance);
    }

    // Phase 22 D-03 — 8-consumer surface. ImportList (#7) returns false until Phase 26
    // flips it to a live _importListFactory.AllForTag() query; AutoTagging (#8) is LIVE
    // as of Phase 24 Plan 24-04 (_autoTaggingService.AllForTag(tag.Id).Count). Validator
    // signature shape preserves the 8-consumer projection so the ImportList diff for
    // Phase 26 stays small (one line per consumer, no class-shape change).
    public class TagInUseValidator : AbstractValidator<Tag>, ITagInUseValidator
    {
        private readonly ITagService _tagService;
        private readonly IAutoTaggingService _autoTaggingService;

        public TagInUseValidator(ITagService tagService, IAutoTaggingService autoTaggingService)
        {
            _tagService = tagService;
            _autoTaggingService = autoTaggingService;

            RuleFor(c => c).Custom((tag, context) =>
            {
                var details = _tagService.Details(tag.Id);
                var consumerSummary = new List<string>();
                var totalCount = 0;

                // Slot 1: Manga (live — _mangaService.AllForTag via TagService.Details)
                var mangaCount = details.MangaIds?.Count ?? 0;
                if (mangaCount > 0)
                {
                    consumerSummary.Add($"{mangaCount} manga");
                    totalCount += mangaCount;
                }

                // Slot 2: DelayProfile (live)
                var delayProfileCount = details.DelayProfileIds?.Count ?? 0;
                if (delayProfileCount > 0)
                {
                    consumerSummary.Add(delayProfileCount == 1
                        ? "1 delay profile"
                        : $"{delayProfileCount} delay profiles");
                    totalCount += delayProfileCount;
                }

                // Slot 3: Notification (live) — D-06 frontend label "connection"
                var notificationCount = details.NotificationIds?.Count ?? 0;
                if (notificationCount > 0)
                {
                    consumerSummary.Add(notificationCount == 1
                        ? "1 connection"
                        : $"{notificationCount} connections");
                    totalCount += notificationCount;
                }

                // Slot 4: ReleaseProfile (live — TagDetails.RestrictionIds is the manga-shape field)
                var releaseProfileCount = details.RestrictionIds?.Count ?? 0;
                if (releaseProfileCount > 0)
                {
                    consumerSummary.Add(releaseProfileCount == 1
                        ? "1 release profile"
                        : $"{releaseProfileCount} release profiles");
                    totalCount += releaseProfileCount;
                }

                // Slot 4b: ExcludedReleaseProfile (live — added 2026-05-17 per PR #197
                // codex P2 + coderabbit Major finding). TagDetails.InUse aggregate
                // includes ExcludedReleaseProfileIds (TagDetails.cs:26) but the original
                // Plan 22-04 validator only checked RestrictionIds, leaving a consumer
                // gap: a tag attached ONLY via excluded-release-profile would slip past
                // the controller pre-check and fall through to TagService.Delete()'s
                // ModelConflictException — inconsistent with the new DELETE validation
                // contract.
                var excludedReleaseProfileCount = details.ExcludedReleaseProfileIds?.Count ?? 0;
                if (excludedReleaseProfileCount > 0)
                {
                    consumerSummary.Add(excludedReleaseProfileCount == 1
                        ? "1 excluded release profile"
                        : $"{excludedReleaseProfileCount} excluded release profiles");
                    totalCount += excludedReleaseProfileCount;
                }

                // Slot 5: Indexer (live)
                var indexerCount = details.IndexerIds?.Count ?? 0;
                if (indexerCount > 0)
                {
                    consumerSummary.Add(indexerCount == 1
                        ? "1 indexer"
                        : $"{indexerCount} indexers");
                    totalCount += indexerCount;
                }

                // Slot 6: DownloadClient (live)
                var downloadClientCount = details.DownloadClientIds?.Count ?? 0;
                if (downloadClientCount > 0)
                {
                    consumerSummary.Add(downloadClientCount == 1
                        ? "1 download client"
                        : $"{downloadClientCount} download clients");
                    totalCount += downloadClientCount;
                }

                // Phase 22 D-03 — ImportList stub. Returns no consumer count until Phase 26
                // flips it to a live _importListFactory.AllForTag() query.

                // Slot 8: AutoTagging (Phase 24 Plan 24-04 — flipped from Phase 22 D-03 stub
                // atomic with V5 AutoTaggingController shipping). Mirrors the sibling consumers'
                // count -> summary -> totalCount aggregation pattern.
                var autoTaggingCount = _autoTaggingService.AllForTag(tag.Id).Count;
                if (autoTaggingCount > 0)
                {
                    consumerSummary.Add(autoTaggingCount == 1
                        ? "1 auto-tagging rule"
                        : $"{autoTaggingCount} auto-tagging rules");
                    totalCount += autoTaggingCount;
                }

                if (consumerSummary.Count == 0)
                {
                    return;
                }

                var itemWord = totalCount == 1 ? "item" : "items";
                context.AddFailure(
                    $"Tag '{tag.Label}' is in use by {totalCount} {itemWord} and cannot be deleted: " +
                    $"{string.Join(", ", consumerSummary)}.");
            });
        }
    }
}
