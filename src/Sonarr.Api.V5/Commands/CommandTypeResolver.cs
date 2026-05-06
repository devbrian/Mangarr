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
    // Phase 11 review WR-05: closed allowlist of TV namespace prefixes that the transitional
    // tv-prefer rule applies to. Anchored on StartsWith — the prior `.Contains(".Tv.")` /
    // `.Contains(".EpisodeImport.")` substring match was too permissive and would misroute
    // manga POSTs to the TV handler if any future Phase 12-13 cleanup work landed a manga
    // command under one of those substrings (e.g. NzbDrone.Core.Manga.TvAdaptation).
    private static readonly string[] TvNamespacePrefixes =
    {
        "NzbDrone.Core.Tv.",
        "NzbDrone.Core.MediaFiles.EpisodeImport.",
    };

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
                c.FullName != null &&
                c.FullName.Equals(contractName, StringComparison.InvariantCultureIgnoreCase))
                ?? throw new BadRequestException(
                    $"Command name '{name}' is ambiguous and supplied contractName " +
                    $"'{contractName}' did not match any of the candidate types.");
        }

        // No contractName supplied — transitional rule until Phase 14 deletes Tv/:
        // prefer the Tv-namespace match for backward compatibility with the existing
        // TV-side UI (frontend/src/InteractiveImport/Interactive/InteractiveImportModalContent.tsx).
        // Future manga-side UI MUST set commandResource.ContractName explicitly.
        //
        // Phase 11 review WR-01 + WR-05: filter via the closed TvNamespacePrefixes allowlist
        // (StartsWith — anchored, deterministic) instead of substring `.Contains(".Tv.")`.
        // If exactly one TV-namespace match exists, route to it. Otherwise — zero matches OR
        // multiple TV matches — throw BadRequestException demanding ContractName. The prior
        // `?? matches[0]` fallback was non-deterministic: KnownTypes.GetImplementations returns
        // types in reflection assembly-load enumeration order, which is not stable across
        // runs and platforms. Throwing forces clients into the ContractName path for any new
        // collision Phase 11 hasn't pre-known about.
        var tvMatches = matches.Where(c => c.FullName != null
                                        && TvNamespacePrefixes.Any(p => c.FullName.StartsWith(p, StringComparison.Ordinal)))
                               .ToList();

        if (tvMatches.Count == 1)
        {
            return tvMatches[0];
        }

        throw new BadRequestException(
            $"Command name '{name}' is ambiguous across {matches.Count} types. " +
            $"Specify ContractName to disambiguate. Candidates: " +
            $"{string.Join(", ", matches.Select(m => m.FullName))}.");
    }
}
