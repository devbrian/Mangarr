using System;
using System.Linq;
using NzbDrone.Common.Composition;
using NzbDrone.Core.Messaging.Commands;
using Sonarr.Http.REST;

namespace Sonarr.Api.V3.Commands
{
    /// <summary>
    /// V3 mirror of <see cref="Sonarr.Api.V5.Commands.CommandTypeResolver"/>.
    /// Duplicated rather than cross-project-referenced because V3 does NOT
    /// project-reference V5 (V3 is the legacy API surface; V5 is the current
    /// surface — referencing V5 from V3 would invert the dependency direction).
    /// </summary>
    /// <remarks>
    /// Phase 14 cleanup-eligible alongside the V5 helper: once <c>Tv/</c> deletes,
    /// the simple-name collision disappears and the multi-match branches become
    /// dead code.
    /// </remarks>
    public static class CommandTypeResolver
    {
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
            if (!string.IsNullOrEmpty(contractName))
            {
                return matches.SingleOrDefault(c =>
                    c.FullName.Equals(contractName, StringComparison.InvariantCultureIgnoreCase))
                    ?? throw new BadRequestException(
                        $"Command name '{name}' is ambiguous and supplied contractName " +
                        $"'{contractName}' did not match any of the candidate types.");
            }

            // No contractName supplied — transitional rule until Phase 14 deletes Tv/:
            // prefer the Tv-namespace match for backward compatibility with the existing
            // V3-era TV-side UI POSTs.
            return matches.SingleOrDefault(c => c.FullName.Contains(".Tv.", StringComparison.Ordinal)
                                              || c.FullName.Contains(".EpisodeImport.", StringComparison.Ordinal))
                ?? matches[0];
        }
    }
}
