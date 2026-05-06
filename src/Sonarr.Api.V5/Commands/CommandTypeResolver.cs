using NzbDrone.Common.Composition;
using NzbDrone.Core.Messaging.Commands;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Commands;

/// <summary>
/// Resolves a CLR <see cref="Command"/>-derived <see cref="Type"/> from the JSON
/// <c>Name</c> (and optional <c>ContractName</c>) fields posted by a V5 caller.
/// Handles the simple-name-collision case where multiple <see cref="Command"/>
/// subclasses share a <see cref="Type.Name"/> (e.g. the
/// <c>EpisodeImport.Manual.ManualImportCommand</c> + <c>MangaImport.Manual.ManualImportCommand</c>
/// pair).
/// </summary>
/// <remarks>
/// Phase 14 cleanup-eligible: once <c>Tv/</c> deletes, the simple-name collision
/// disappears and the multi-match branches become dead code. Plan 11-08 close-out
/// records this for the Phase-14 cascade.
/// </remarks>
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
