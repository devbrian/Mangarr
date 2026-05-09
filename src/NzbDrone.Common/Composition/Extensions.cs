using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DryIoc;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Common.Composition.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static Rules WithNzbDroneRules(this Rules rules)
        {
            return rules.WithMicrosoftDependencyInjectionRules()
                .WithAutoConcreteTypeResolution()
                .WithDefaultReuse(Reuse.Singleton);
        }

        public static IContainer AddStartupContext(this IContainer container, StartupContext context)
        {
            container.RegisterInstance<IStartupContext>(context, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
            return container;
        }

        public static IContainer AutoAddServices(this IContainer container, List<string> assemblyNames)
        {
            var assemblies = AssemblyLoader.Load(assemblyNames);

            container.RegisterMany(assemblies,
                serviceTypeCondition: type => type.IsInterface && !string.IsNullOrWhiteSpace(type.FullName) && !type.FullName.StartsWith("System"),
                reuse: Reuse.Singleton);

            container.RegisterMany(assemblies,
                serviceTypeCondition: type => !type.IsInterface && !string.IsNullOrWhiteSpace(type.FullName) && !type.FullName.StartsWith("System"),
                reuse: Reuse.Transient);

            var knownTypes = new KnownTypes(assemblies.SelectMany(x => x.GetTypes()).ToList());
            container.RegisterInstance(knownTypes);

            // === BEGIN PHASE 16 REG-DUMP P-14-DRY (REMOVE BEFORE PLAN 16-07 CLOSE-OUT MERGE) ===
            // Phase 16 D-28-style REG-DUMP capture per Phase 15 Plan 15-01 carry-over.
            // Emits each DryIoc registration as an NLog Debug-level "REG-DUMP:" log line at app boot.
            // Scrape via:
            //   grep '^.*REG-DUMP:' "$logFile" | sed 's/^[^|]*|[^|]*|[^|]*|REG-DUMP: /REG-DUMP: /' | sort -u > regdump.txt
            // DELETE THIS BLOCK BEFORE THE PHASE 16 CLOSE-OUT MERGE (Plan 16-07).
            // NOTE: the existing Phase 15 D-28 env-var-gated block below also emits REG-DUMP
            // lines (one-shot to a file when MANGARR__REGDUMP=<path> is set). Both paths produce
            // identical "REG-DUMP: <iface> -> <impl> [<reuse>]" line shape; either may be used to
            // capture the Plan 16-01 pre-baseline. The env-var path is preferred for clean
            // line-level output (no NLog timestamp prefix). Plan 16-07 removes both blocks.
            var regDumpLogger = NLog.LogManager.GetLogger("RegDump");
            foreach (var reg in container.GetServiceRegistrations())
            {
                var serviceType = reg.ServiceType?.FullName ?? "<null>";
                var implType = reg.ImplementationType?.FullName ?? "<factory>";
                var reuse = reg.Factory?.Reuse?.GetType().Name ?? "<default>";
                regDumpLogger.Debug($"REG-DUMP: {serviceType} -> {implType} [{reuse}]");
            }

            // === END PHASE 16 REG-DUMP P-14-DRY ===

            // Sonarr divergence: Phase 15 D-28 — REG-DUMP capture hook for the per-wave REG-DUMP
            // contract (Phase 14 dress-rehearsal baseline at 1999 entries; expected delta -200 to -500
            // post-Phase-15 TV-only deletions). Activated by env var MANGARR__REGDUMP=<path>; one-shot
            // dump of container.GetServiceRegistrations() in baseline format
            // "REG-DUMP: <iface> -> <impl> [<reuse>]". No-op when env var unset (zero-cost in production).
            var regdumpPath = Environment.GetEnvironmentVariable("MANGARR__REGDUMP");
            if (!string.IsNullOrEmpty(regdumpPath))
            {
                try
                {
                    var lines = container.GetServiceRegistrations()
                        .Select(r =>
                        {
                            var iface = r.ServiceType?.FullName ?? "<unknown>";
                            var impl = r.Factory?.ImplementationType?.FullName ?? r.Factory?.GetType().Name ?? "<unknown>";
                            var reuse = r.Factory?.Reuse?.GetType().Name ?? "Transient";
                            return $"REG-DUMP: {iface} -> {impl} [{reuse}]";
                        })
                        .OrderBy(l => l, StringComparer.Ordinal)
                        .ToList();
                    File.WriteAllLines(regdumpPath, lines);
                }
                catch
                {
                    // Best-effort planning artifact; do not fail boot if dump fails.
                }
            }

            return container;
        }
    }
}
