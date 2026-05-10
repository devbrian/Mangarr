// tools/ChromiumPrefetch/Program.cs
//
// Build-time helper invoked by:
//   - distribution/docker-build/Dockerfile (Wave 2 / Plan 17-03)
//   - .github/workflows/source-soak.yml (Wave 3 / Plan 17-04)
//
// Downloads the PuppeteerSharp-pinned Chromium revision into a caller-specified
// output directory. Pinning the revision to PuppeteerSharp's expected version
// guarantees DevTools Protocol compatibility with the runtime signer
// (Phase 17 D-02).
//
// Usage:
//   dotnet run --project tools/ChromiumPrefetch/ChromiumPrefetch.csproj \
//     --configuration Release \
//     -- --output-dir /opt/mangarr-chromium
//
// Default output-dir: /opt/mangarr-chromium (Phase 17 D-03 — fixed image-layer
// path, NOT under /config). Caller may override for sandboxed builds (e.g.
// $RUNNER_TEMP/mangarr-chromium in CI).

using PuppeteerSharp;

var outIdx = Array.IndexOf(args, "--output-dir");
var outputDir = outIdx >= 0 && outIdx + 1 < args.Length
    ? args[outIdx + 1]
    : "/opt/mangarr-chromium";

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("ChromiumPrefetch: download PuppeteerSharp-pinned Chromium revision.");
    Console.WriteLine("Usage: ChromiumPrefetch [--output-dir <path>]");
    Console.WriteLine("Default output-dir: /opt/mangarr-chromium");
    return 0;
}

var fetcher = new BrowserFetcher(new BrowserFetcherOptions { Path = outputDir });
var installed = await fetcher.DownloadAsync();
Console.WriteLine($"Chromium downloaded to: {installed.GetExecutablePath()}");
return 0;
