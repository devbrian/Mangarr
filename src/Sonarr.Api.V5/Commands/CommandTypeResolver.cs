using NzbDrone.Common.Composition;
using NzbDrone.Core.Messaging.Commands;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Commands;

// Resolves a CLR Command-derived Type from the JSON Name (and optional ContractName)
// fields posted by a V5 caller. Handles the simple-name-collision case where multiple
// Command subclasses share a Type.Name (e.g. the EpisodeImport.Manual.ManualImportCommand
// + MangaImport.Manual.ManualImportCommand pair).
//
// Phase 14 cleanup-eligible: once Tv/ deletes, the simple-name collision disappears
// and the multi-match branches become dead code. Plan 11-08 close-out records this
// for the Phase-14 cascade.
public static class CommandTypeResolver
{
    public static Type Resolve(KnownTypes knownTypes, string? name, string? contractName)
    {
        var matches = knownTypes.GetImplementations(typeof(Command))
            .Where(c => c.Name.Replace("Command", "")
                         .Equals(name, StringComparison.InvariantCultureIgnoreCase))
            .ToList();

        if (matches.Count == 0)
        {
            throw new NotFoundException();
        }

        if (matches.Count == 1)
        {
            return matches[0];
        }

        // Multi-match: simple Name collision (e.g. ManualImportCommand exists in both
        // EpisodeImport.Manual and MangaImport.Manual namespaces). Discriminate by FullName.
        if (!string.IsNullOrEmpty(contractName))
        {
            return matches.SingleOrDefault(c =>
                c.FullName!.Equals(contractName, StringComparison.InvariantCultureIgnoreCase))
                ?? throw new BadRequestException(
                    $"Command name '{name}' is ambiguous and supplied contractName " +
                    $"'{contractName}' did not match any of the candidate types.");
        }

        // No contractName supplied — transitional rule until Phase 14 deletes Tv/:
        // prefer the Tv-namespace match for backward compatibility with the existing
        // TV-side UI (frontend/src/InteractiveImport/Interactive/InteractiveImportModalContent.tsx).
        // Future manga-side UI MUST set commandResource.ContractName explicitly.
        // Uses .Tv. OR .EpisodeImport. so the manual-import collision (which lives under
        // MediaFiles/EpisodeImport/, not under MediaFiles/Tv/) is correctly biased.
        return matches.SingleOrDefault(c => c.FullName!.Contains(".Tv.", StringComparison.Ordinal)
                                          || c.FullName!.Contains(".EpisodeImport.", StringComparison.Ordinal))
            ?? matches[0];
    }
}
