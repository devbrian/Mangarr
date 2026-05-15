// Phase 3 D-15 / D-16 — soak harness CLI.
// Exercises configured Phase 3 indexers (MangaDex + comix.to per slate update D-19) against a
// fixed list of smoke-test manga. Records HTTP success/failure rate + p95 latency per source.
// Appends a markdown row to .planning/phases/03-*/SOURCE-PROBE-{slug}.md cumulative table.
//
// CLI:
//   dotnet run --project SoakRunner.csproj -- \
//     --sources "mangadex comix.to" \
//     --titles "One Piece;Berserk;Solo Leveling" \
//     --output ".planning/phases/03-indexer-contract-aggregator-sources/"

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Mangarr.Soak;

public static class SoakRunner
{
    public static async Task<int> Main(string[] args)
    {
        var arg = ParseArgs(args);
        var sources = arg["sources"].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var titles = arg["titles"].Split(';', StringSplitOptions.RemoveEmptyEntries);
        var outputDir = arg["output"];

        Console.WriteLine($"[soak] sources={string.Join(",", sources)}; titles={string.Join(",", titles)}; output={outputDir}");

        var http = new HttpClient();
        http.DefaultRequestHeaders.Add("User-Agent", "Mangarr/0.1");

        foreach (var source in sources)
        {
            var slug = source.Replace(".", "").ToLowerInvariant();   // "comix.to" -> "comixto" -> filename uses "comix"
            var probePath = Path.Combine(outputDir, $"SOURCE-PROBE-{(slug == "comixto" ? "comix" : slug)}.md");
            var (smokeOk, requests, failures, p95Ms) = await ExerciseSource(source, titles, http);
            var failurePct = requests > 0 ? (double)failures / requests : 0.0;
            var notes = smokeOk ? "clean" : "smoke-test failures";
            var row = $"| {DateTime.UtcNow:yyyy-MM-dd} | scheduled | {(smokeOk ? "3/3" : "<3/3")} | {requests} | {failures} | {failurePct:P1} | {p95Ms}ms | {notes} |";
            await AppendRow(probePath, row);
        }

        return 0;
    }

    private static async Task<(bool SmokeOk, int Requests, int Failures, double P95Ms)> ExerciseSource(string source, string[] titles, HttpClient http)
    {
        var latencies = new List<double>();
        var failures = 0;
        foreach (var title in titles)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                // For each source, hit the documented public endpoint with the title query.
                // Phase 3-minimal: hit MangaDex /manga search; comix.to /api/v1/manga search
                // (camelCase under /api/v1/, not the Phase 3 plan literal /api/v2/ —
                // see ComixParser.cs and ComixRequestGenerator.cs).
                // Real implementation reuses MangaDexIndexer / ComixIndexer via DI — this CLI's
                // simplified version probes the public endpoints directly.
                var url = source switch
                {
                    "mangadex" => $"https://api.mangadex.org/manga?title={Uri.EscapeDataString(title)}&limit=1",
                    "comix.to" => $"https://comix.to/api/v1/manga?keyword={Uri.EscapeDataString(title)}&limit=1&page=1",
                    _ => null
                };
                if (url == null) continue;
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                if (source == "comix.to") req.Headers.Referrer = new Uri("https://comix.to/");
                using var resp = await http.SendAsync(req);
                if (!resp.IsSuccessStatusCode) failures++;
            }
            catch
            {
                failures++;
            }
            sw.Stop();
            latencies.Add(sw.Elapsed.TotalMilliseconds);
            // Honor per-source rate budget approximately: MangaDex 1.5s; comix.to 200ms.
            var delayMs = source == "mangadex" ? 1500 : 200;
            await Task.Delay(delayMs);
        }
        latencies.Sort();
        var p95 = latencies.Count > 0 ? latencies[(int)Math.Floor(latencies.Count * 0.95)] : 0.0;
        var smokeOk = failures == 0;
        return (smokeOk, titles.Length, failures, Math.Round(p95));
    }

    private static async Task AppendRow(string path, string row)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"[soak] WARNING: {path} does not exist; skipping append");
            return;
        }
        await File.AppendAllLinesAsync(path, new[] { row });
        Console.WriteLine($"[soak] appended row to {path}");
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
