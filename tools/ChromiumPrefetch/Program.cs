// tools/ChromiumPrefetch/Program.cs
//
// Build-time helper invoked by:
//   - distribution/docker-build/Dockerfile (Wave 2 / Plan 17-03)
//   - distribution/docker/Dockerfile (Phase 21 — runtime image chromium-builder stage)
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
//     -- --output-dir /opt/mangarr-chromium [--platform <name>]
//
// --output-dir defaults to /opt/mangarr-chromium (Phase 17 D-03 — fixed image-
//   layer path, NOT under /config). Caller may override for sandboxed builds
//   (e.g. $RUNNER_TEMP/mangarr-chromium in CI).
//
// --platform <name> forces the BrowserFetcher to download a specific platform
//   build (case-insensitive). Accepted values mirror PuppeteerSharp's Platform
//   enum: linux | linux-arm64 | linux_arm64 | linuxarm64 | macos | macosarm64 |
//   win32 | win64. If omitted, BrowserFetcher autodetects the current process
//   architecture — which fails for Docker multi-arch builds where the builder
//   stage runs on the BUILD platform (amd64) but the runtime stage needs the
//   target-arch Chrome (Phase 21 v1.0.0 deploy.yml regression — arm64 stage
//   COPYed an amd64 binary and `chrome --version` failed with `not found`).
//
// Sonarr divergence: arch-aware platform selection is Mangarr-specific; the
// upstream PuppeteerSharp pattern in Sonarr is single-platform Linux only
// (Sonarr has no headless-browser dependency to ship in a multi-arch image).

using PuppeteerSharp;

var outIdx = Array.IndexOf(args, "--output-dir");
var outputDir = outIdx >= 0 && outIdx + 1 < args.Length
    ? args[outIdx + 1]
    : "/opt/mangarr-chromium";

var platIdx = Array.IndexOf(args, "--platform");
var platformArg = platIdx >= 0 && platIdx + 1 < args.Length
    ? args[platIdx + 1]
    : null;

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("ChromiumPrefetch: download PuppeteerSharp-pinned Chromium revision.");
    Console.WriteLine("Usage: ChromiumPrefetch [--output-dir <path>] [--platform <name>]");
    Console.WriteLine("Default output-dir: /opt/mangarr-chromium");
    Console.WriteLine("Platforms: linux | linux-arm64 | macos | macosarm64 | win32 | win64");
    Console.WriteLine("(--platform omitted → BrowserFetcher autodetects the build-host arch)");
    return 0;
}

var options = new BrowserFetcherOptions { Path = outputDir };

if (!string.IsNullOrWhiteSpace(platformArg))
{
    // Normalize: lowercase, strip non-alphanumeric so callers can pass
    // "linux-arm64", "linux_arm64", "linuxarm64", "LinuxArm64" interchangeably.
    var normalized = new string(platformArg.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    Platform parsed = normalized switch
    {
        "linux" or "linuxx64" or "linux64" or "amd64" or "x64" => Platform.Linux,
        "linuxarm64" or "arm64" or "aarch64" => Platform.LinuxArm64,
        "macos" or "macosx64" or "darwin" => Platform.MacOS,
        "macosarm64" or "macosaarch64" or "darwinarm64" => Platform.MacOSArm64,
        "win32" or "windowsx86" or "windows32" => Platform.Win32,
        "win64" or "windowsx64" or "windows64" => Platform.Win64,
        _ => throw new ArgumentException(
            $"Unknown --platform value '{platformArg}'. Valid: linux | linux-arm64 | macos | macosarm64 | win32 | win64")
    };
    options.Platform = parsed;
    Console.WriteLine($"BrowserFetcher platform override: --platform {platformArg} → {parsed}");
}

var fetcher = new BrowserFetcher(options);
var installed = await fetcher.DownloadAsync();
Console.WriteLine($"Chromium downloaded to: {installed.GetExecutablePath()}");
return 0;
