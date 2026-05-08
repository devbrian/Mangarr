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
