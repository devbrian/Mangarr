using System;
using System.Linq;
using NzbDrone.Common.Composition;
using NzbDrone.Core.Messaging.Commands;
using Sonarr.Http.REST;

namespace Sonarr.Api.V3.Commands
{
    // V3 mirror of Mangarr.Api.V5.Commands.CommandTypeResolver. Duplicated rather than
    // cross-project-referenced because V3 does NOT project-reference V5 (V3 is the
    // legacy API surface; V5 is the current surface — referencing V5 from V3 would
    // invert the dependency direction).
    //
    // Phase 14 cleanup-eligible alongside the V5 helper: once Tv/ deletes, the
    // simple-name collision disappears and the multi-match branches become dead code.
    public static class CommandTypeResolver
    {
        // Phase 11 review WR-05: closed allowlist of TV namespace prefixes that the transitional
        // tv-prefer rule applies to. Anchored on StartsWith — the prior substring match was too
        // permissive (see V5 mirror for full rationale).
        private static readonly string[] TvNamespacePrefixes =
        {
            "NzbDrone.Core.Tv.",
            "NzbDrone.Core.MediaFiles.EpisodeImport.",
        };

        public static Type Resolve(KnownTypes knownTypes, string name, string contractName)
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

            // Multi-match: simple Name collision. Discriminate by FullName when contractName supplied.
            // Phase 11 review WR-02: V3 is not under <Nullable>enable</Nullable>, so c.FullName is
            // not statically annotated. Type.FullName CAN be null per CLR contract (constructed
            // generic types whose type arg has no FullName, anonymous types). For Command-derived
            // application classes this isn't realistic, but the explicit null-guard removes a
            // latent NRE that the V5 mirror suppresses with `!`.
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
            // V3-era TV-side UI POSTs.
            //
            // Phase 11 review WR-01 + WR-05: closed TvNamespacePrefixes allowlist (anchored
            // StartsWith match). Throw BadRequestException for ambiguous-without-tv-bias rather
            // than the prior `?? matches[0]` fallback — KnownTypes.GetImplementations order is
            // determined by reflection assembly-load enumeration, not stable across runs.
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
}
