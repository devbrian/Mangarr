// Phase 3 D-15 / D-16 — threshold checker CLI.
// Reads the cumulative soak results table from each SOURCE-PROBE-{slug}.md; computes
// failure% over the last N days; fails the run with non-zero exit code if any source
// exceeds the threshold. Surfaces as a Health Check warning post-merge per D-15.
//
// CLI:
//   dotnet run --project SoakThresholdChecker.csproj -- \
//     --window 7 --threshold 0.05 \
//     --probe-dir .planning/phases/03-indexer-contract-aggregator-sources/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Mangarr.Soak;

public static class SoakThresholdChecker
{
    public static int Main(string[] args)
    {
        var arg = ParseArgs(args);
        var window = int.Parse(arg["window"], CultureInfo.InvariantCulture);
        var threshold = double.Parse(arg["threshold"], CultureInfo.InvariantCulture);
        var probeDir = arg["probe-dir"];

        var failed = false;
        foreach (var probe in Directory.EnumerateFiles(probeDir, "SOURCE-PROBE-*.md"))
        {
            // Skip the master index
            if (Path.GetFileName(probe).Equals("SOURCE-PROBE.md", StringComparison.OrdinalIgnoreCase)) continue;

            var text = File.ReadAllText(probe);
            // Match cumulative-table rows: | YYYY-MM-DD | run | ... | Failure% | ...
            var rows = Regex.Matches(text, @"^\|\s*(\d{4}-\d{2}-\d{2})\s*\|[^|]*\|[^|]*\|[^|]*\|[^|]*\|\s*([\d.]+)%\s*\|", RegexOptions.Multiline);

            var cutoff = DateTime.UtcNow.AddDays(-window);
            var inWindow = rows.Cast<Match>()
                .Where(m => DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) && d >= cutoff)
                .Select(m => double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) / 100.0)
                .ToList();

            if (inWindow.Count == 0)
            {
                Console.WriteLine($"[threshold] {Path.GetFileName(probe)}: no rows in last {window}d (skipped)");
                continue;
            }

            var avg = inWindow.Average();
            var status = avg <= threshold ? "OK" : "FAIL";
            Console.WriteLine($"[threshold] {Path.GetFileName(probe)}: avg failure% over last {window}d = {avg:P2} ({status}; threshold={threshold:P0})");

            if (avg > threshold) failed = true;
        }

        return failed ? 1 : 0;
    }

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var dict = new Dictionary<string, string>();
        for (var i = 0; i < args.Length - 1; i += 2)
        {
            if (args[i].StartsWith("--")) dict[args[i].TrimStart('-')] = args[i + 1];
        }
        return dict;
    }
}
