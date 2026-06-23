using FluentValidation;

namespace Mangarr.Api.V5.Discovery;

// Phase 42 — FluentValidation for the Discovery bulk-add request (PR #396 review #3).
//
// The bulk-add endpoint is fire-and-forget (202, D-07): an invalid payload that slips
// past the boundary fails LATER in background command execution where the user never
// sees it. So the controller validates the payload SHAPE here BEFORE enqueue (mirrors
// the search endpoint's validator-in-controller clamp) and returns 400 on any violation
// instead of accepting a doomed command with 202.
//
// Required: a non-empty MangaBakaIds list, a root folder path, a known Monitor value, and
// positive profile ids (the add fan-out resolves both by id; 0/negative is never valid).
public class DiscoveryBulkAddResourceValidator : AbstractValidator<DiscoveryBulkAddResource>
{
    public DiscoveryBulkAddResourceValidator()
    {
        RuleFor(r => r.MangaBakaIds).NotEmpty()
            .WithMessage("mangaBakaIds must contain at least one id.");

        RuleFor(r => r.RootFolderPath).NotEmpty()
            .WithMessage("rootFolderPath is required.");

        RuleFor(r => r.Monitor).IsInEnum()
            .WithMessage("monitor is not a valid value.");

        RuleFor(r => r.TranslationProfileId).GreaterThan(0)
            .WithMessage("translationProfileId must be a positive id.");

        RuleFor(r => r.CustomFormatProfileId).GreaterThan(0)
            .WithMessage("customFormatProfileId must be a positive id.");
    }
}
