using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Framework;
using NzbDrone.Test.Common;
using NzbDrone.Test.Common.Datastore;

namespace NzbDrone.Automation.Test;

[TestFixture]
[AutomationTest]
public abstract class AutomationTest
{
    private NzbDroneRunner _runner;
    private PostgresOptions _postgresOptions;

    protected IBrowserContext Context { get; private set; }
    protected IPage Page { get; private set; }
    protected string RootUri => $"http://localhost:{_runner.Port}";
    protected string ApiKey => _runner.ApiKey;
    protected NzbDroneRunner Runner => _runner;

    /// <summary>
    /// Optional override for fixtures that need to boot the runner under a
    /// configured UrlBase (reverse-proxy / hosted-under-prefix scenarios). Default
    /// is empty, matching the legacy single-instance-no-prefix harness shape.
    /// When set to a bare segment (e.g. <c>"mangarr"</c>) the runner writes
    /// UrlBase to config.xml before boot, all asset / API / SignalR paths shift
    /// to <c>http://localhost:port/{UrlBase}/...</c>, and SeedBaselineAsync +
    /// initial Page.GotoAsync target the prefixed root. GH #174 regression
    /// pattern; see UrlBaseRedirectFixture for the canonical consumer.
    /// </summary>
    protected virtual string ConfiguredUrlBase => string.Empty;

    /// <summary>
    /// GH #268: the automation tier seeds the Comix indexer DISABLED by default
    /// (the final step of <see cref="TestKit.TestKit.SeedBaselineAsync(bool)"/>) —
    /// comix.to cannot be HTTP-cassette'd (Phase 18 D-11), so an un-cassetted
    /// indexer fan-out (InteractiveSearch / add-manga conditional backfill search /
    /// ImportListSync) would escape to the live network. This safe-by-default
    /// baseline replaces the ~92 per-fixture <c>DisableComixIndexerAsync</c> calls.
    /// Fixtures that DO exercise Comix offline via <c>CassettingComixSigner</c>
    /// (recorded cassettes under <c>Fixtures/Cassettes/Comix/</c>) override this to
    /// <c>false</c> so the seeded Comix indexer stays enabled for their fan-out.
    /// </summary>
    protected virtual bool DisableComixIndexerInBaseline => true;

    /// <summary>
    /// Convenience accessor — combines RootUri with the runner's UrlBase so
    /// fixtures and seed code can write <c>$"{HostBaseUrl}/..."</c> instead of
    /// reassembling the prefix. Equal to RootUri when ConfiguredUrlBase is empty.
    /// </summary>
    protected string HostBaseUrl => string.IsNullOrEmpty(_runner.UrlBase)
        ? RootUri
        : $"{RootUri}/{_runner.UrlBase}";

    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        // CI-infra F1 fix (ci-test-jobs-latent-faults): PlaywrightSetUpFixture now tolerates a
        // missing Playwright driver/browser (so the unit-tier TestKit fixtures pass in the
        // unit_test job). The automation tier genuinely needs a Browser — fail fast here with a
        // clear message instead of a bare NullReferenceException on Browser.NewContextAsync.
        Assert.That(
            PlaywrightSetUpFixture.BrowserAvailable,
            Is.True,
            "Playwright browser was not provisioned for this job. AutomationTest fixtures must run "
            + "in a job that installs the Playwright driver/browser (automation_test_* jobs), not "
            + "the unit_test job. See PlaywrightSetUpFixture.SetUpAsync.");

        // /gsd-debug nightly-automation-fail Pattern B3 fix: the automation_test_nightly
        // postgres matrix entries set Mangarr__Postgres__Host/Port/User/Password env vars
        // via the composite action, but NOT MainDb/LogDb. Previously this harness passed
        // PostgresOptions=null into NzbDroneRunner, which made the runner skip its env-var
        // setup block (`if (PostgresOptions?.Host != null)` at NzbDroneRunner.cs:212). The
        // child Mangarr process then fell back to ConfigFileProvider's hardcoded defaults
        // (`"mangarr-main"` / `"mangarr-log"` post-Phase-15 rebrand) — but the postgres
        // server in CI has only the default `postgres` DB, and nothing in production code
        // calls CREATE DATABASE. Kestrel never bound, the readiness probe timed out for
        // 60s × N tests, and the job hit GH's 1h job-level cancel.
        //
        // Mirror the unit_test_postgres pattern in NzbDrone.Core.Test/Framework/DbTest.cs:
        //   1. PostgresDatabase.GetTestOptions() reads env vars, derives unique-per-run
        //      MainDb/LogDb names from TestBase.GetUID() (PID+ticks+seq) so parallel
        //      matrix runs cannot collide.
        //   2. When Host is populated (postgres mode), pre-create both DBs server-side.
        //      When Host is null/empty (sqlite mode), GetTestOptions returns a sentinel
        //      that the runner detects and skips the entire env-var block — sqlite path
        //      is unchanged.
        //   3. Pass the populated options into NzbDroneRunner so its env-var block fires
        //      and the child Mangarr inherits MainDb/LogDb pointing at the just-created
        //      databases.
        _postgresOptions = PostgresDatabase.GetTestOptions();
        if (_postgresOptions.Host.IsNotNullOrWhiteSpace())
        {
            PostgresDatabase.Create(_postgresOptions, MigrationType.Main);
            PostgresDatabase.Create(_postgresOptions, MigrationType.Log);
        }

        _runner = new NzbDroneRunner(LogManager.GetCurrentClassLogger(), _postgresOptions);
        _runner.KillAll();
        _runner.Start(enableAuth: true, urlBase: ConfiguredUrlBase);

        // D-07 pre-seed baseline (Plan 18-14 D-C fix): root folder + InProcess
        // download client must exist before the browser opens or the AddManga
        // modal's Add button POST fails its required-field validation
        // (RootFolderPath is mandatory; TranslationProfile/CustomFormatProfile
        // come from Phase 5 baseline migration). Without this seed, every
        // AddMangaFlow.AddByMangaDexIdAsync call times out at ConfirmAddAsync.
        var seedRoot = Path.Combine(_runner.AppData, "MangaLibrary");
        Directory.CreateDirectory(seedRoot);

        // GH #174: TestKit builds its REST client URL from rootUri; when urlBase
        // is configured we point it at the prefixed API path so the seed calls
        // hit `{rootUri}/{urlBase}/api/v5/...` instead of getting 307-redirected
        // by UrlBaseMiddleware (RestSharp does not auto-follow 307 with method
        // preservation on POST/PUT).
        await new NzbDrone.Automation.Test.TestKit.TestKit(HostBaseUrl, _runner.ApiKey, seedRoot)
            .SeedBaselineAsync(disableComixIndexer: DisableComixIndexerInBaseline);

        Context = await PlaywrightSetUpFixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
            ExtraHTTPHeaders = new Dictionary<string, string> { ["X-Api-Key"] = _runner.ApiKey }
        });

        await Context.Tracing.StartAsync(new TracingStartOptions
        {
            Screenshots = true,
            Snapshots = true,
            Sources = true
        });

        Page = await Context.NewPageAsync();

        // GH #174: when urlBase is configured the SPA is hosted under it; targeting
        // RootUri here would trip UrlBaseMiddleware's 307 redirect and depend on
        // Playwright following it, which it does — but the test fixtures that
        // exercise the redirect itself (UrlBaseRedirectFixture) need to control
        // navigation explicitly, so the base navigation uses the prefixed root.
        await Page.GotoAsync(HostBaseUrl);

        // Wait for app shell ready — `app-shell` testid is annotated in frontend/src/App/PageContent.tsx
        // by Plan-04 wrapper sweep. If the testid is not yet present (Wave 1 before wrappers landed),
        // fall back to title check. Use a shorter timeout for the testid probe so the harness boots
        // quickly when the testid hasn't been wired yet — the fallback assertion is the real gate.
        try
        {
            await Page.GetByTestId("app-shell").WaitForAsync(new LocatorWaitForOptions { Timeout = 5_000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            // Fallback: assert page title contains "Mangarr" until app-shell testid is wired.
            // Both Microsoft.Playwright.TimeoutException (PlaywrightException subclass) AND
            // System.TimeoutException (raised by some transport-layer timeouts) are caught here.
            await Assertions.Expect(Page).ToHaveTitleAsync(new Regex("Mangarr"));
        }
    }

    [TearDown]
    public async Task TearDownAsync()
    {
        // BL-02 (18-REVIEW): when OneTimeSetUp fails before `Page = await Context.NewPageAsync()`
        // (e.g. SeedBaselineAsync rejected by a 4xx, or Browser.NewContextAsync threw), individual
        // [Test] methods still receive a TearDown attempt. Without this null-guard, ScreenshotAsync
        // NREs and masks the original setup failure in CI artifacts.
        if (Page == null)
        {
            return;
        }

        if (TestContext.CurrentContext.Result.FailCount > 0)
        {
            var screenshotPath = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "screenshots",
                $"{TestContext.CurrentContext.Test.FullName}.png");
            Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath)!);
            await Page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });
        }
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDownAsync()
    {
        try
        {
            // BL-01 (18-REVIEW): guard Context inside the try so a failed OneTimeSetUp
            // (before Context = NewContextAsync) doesn't NRE on `Context.Tracing.StopAsync`
            // and mask the underlying setup exception. The finally block still runs
            // _runner?.KillAll() either way, so process cleanup is unaffected.
            if (Context != null)
            {
                var tracePath = Path.Combine(
                    TestContext.CurrentContext.TestDirectory,
                    "traces",
                    $"{GetType().Name}.zip");
                Directory.CreateDirectory(Path.GetDirectoryName(tracePath)!);
                await Context.Tracing.StopAsync(new TracingStopOptions { Path = tracePath });
            }
        }
        finally
        {
            // issue #133 RESOLVED: the #132 try/catch (EntryPointNotFoundException) workaround was
            // removed here once the Microsoft.Bcl.AsyncInterfaces shared-_tests/-dir polyfill was
            // properly fixed (explicit netstandard2.1 type-forwarder pin in src/Directory.Build.props
            // so coverlet.collector's bundled netstandard2.0 polyfill no longer ships into _tests/).
            // Context.DisposeAsync() now binds the in-box .NET 10 IAsyncDisposable and no longer throws.
            if (Context != null)
            {
                await Context.DisposeAsync();
            }

            // GH #252: kill the child Mangarr.Console UNCONDITIONALLY (even when the
            // try block above threw on Tracing.StopAsync, or when OneTimeSetUp failed
            // mid-way). KillAll() reaps _nzbDroneProcess + any stray Mangarr.Console /
            // Mangarr by name, releasing the bound port so the NEXT fixture's
            // NzbDroneRunner can bind. This is the primary lever for leak class 3
            // (Mangarr.Console port-bind). Log what we did so a CI trace shows the
            // teardown ran (the leak symptom is teardown SILENTLY not running).
            if (_runner != null)
            {
                TestContext.Progress.WriteLine(
                    $"[GH#252] OneTimeTearDown: killing NzbDroneRunner (port {_runner.Port}) + stray Mangarr.Console/Mangarr.");
                _runner.KillAll();
            }
            else
            {
                TestContext.Progress.WriteLine(
                    "[GH#252] OneTimeTearDown: runner was null (OneTimeSetUp failed before construction); nothing to kill.");
            }

            // GH #252: sweep orphan Puppeteer/Playwright Chromium. The child Mangarr's
            // ComixPlaywrightSigner owns an embedded Chromium that self-disposes via its
            // IHandle<ApplicationShutdownRequested> handler — but a HARD kill of the child
            // (above, or on a crash) skips that graceful path, orphaning the Chromium to
            // PID 1. The Playwright browser worker for THIS fixture is owned by
            // PlaywrightSetUpFixture and disposed there; this sweep is the catch-all for
            // signer-Chromium leaks. We delegate to scripts/kill-orphan-chromium.ps1, which
            // discriminates leaked Chromium from the user's real Chrome by three orthogonal
            // signals — we never broaden that filter and never `taskkill /F /IM chrome.exe`.
            // Best-effort + fully wrapped so a sweep failure can NEVER mask the test outcome.
            await SweepOrphanChromiumAsync();
            CleanupPostgresDatabases();
        }
    }

    /// <summary>
    /// GH #252: best-effort sweep of leaked PuppeteerSharp/Playwright Chromium left by a
    /// hard-killed child Mangarr (its ComixPlaywrightSigner Chromium never got the graceful
    /// ApplicationShutdownRequested teardown). Windows-only (the script uses
    /// Get-CimInstance / Stop-Process); a no-op elsewhere. NEVER throws — a sweep failure
    /// must not influence the test verdict.
    /// </summary>
    private static async Task SweepOrphanChromiumAsync()
    {
        try
        {
            if (!OsInfo.IsWindows)
            {
                return;
            }

            // Resolve the repo-root kill-orphan-chromium.ps1 relative to the test output
            // dir: _tests/net10.0/ → repo root is ../../ then scripts/.
            var scriptPath = Path.GetFullPath(Path.Combine(
                TestContext.CurrentContext.TestDirectory, "..", "..", "scripts", "kill-orphan-chromium.ps1"));

            if (!File.Exists(scriptPath))
            {
                // Not fatal — the script-level gate (phase-smoke-gate / audit-new-fixtures
                // pre-flight) is the primary sweep; this in-teardown sweep is defense-in-depth.
                TestContext.Progress.WriteLine(
                    $"[GH#252] OneTimeTearDown: kill-orphan-chromium.ps1 not found at {scriptPath}; skipping in-test Chromium sweep.");
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = "pwsh",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-File");
            psi.ArgumentList.Add(scriptPath);
            psi.ArgumentList.Add("-Kill");

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                TestContext.Progress.WriteLine("[GH#252] OneTimeTearDown: failed to start pwsh for Chromium sweep.");
                return;
            }

            // Drain stdout/stderr concurrently. kill-orphan-chromium.ps1 emits a
            // per-process survey; with many leaked Chromium candidates + long
            // executable paths this can exceed the OS pipe buffer. If we don't read
            // the streams, pwsh blocks on write, WaitForExit times out, and we'd kill
            // the sweep mid-Stop-Process — leaving the very leaks it should remove
            // (Codex PR #284 P2). Reading them keeps the child unblocked.
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            // Bound the wait — the sweep is a survey + Stop-Process, sub-second normally.
            // WaitForExitAsync (not the blocking overload) — this runs inside async teardown.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                await proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                }
                catch
                {
                    // ignore — best-effort
                }

                TestContext.Progress.WriteLine("[GH#252] OneTimeTearDown: Chromium sweep timed out (>15s); abandoned.");
                return;
            }

            // Process has exited — the drain tasks complete promptly now.
            await Task.WhenAll(stdoutTask, stderrTask);

            TestContext.Progress.WriteLine(
                $"[GH#252] OneTimeTearDown: Chromium sweep exit={proc.ExitCode}.");
        }
        catch (Exception ex)
        {
            // Swallow ALL exceptions: the sweep is cleanup, not a test assertion.
            TestContext.Progress.WriteLine($"[GH#252] OneTimeTearDown: Chromium sweep error (non-fatal): {ex.Message}");
        }
    }

    /// <summary>
    /// /gsd-debug nightly-automation-fail Pattern B3 fix: drop the per-run postgres
    /// databases that OneTimeSetUp created so the postgres server doesn't accumulate
    /// <c>&lt;run-uid&gt;_main</c> / <c>&lt;run-uid&gt;_log</c> DBs across nightly runs. Mirrors
    /// DbTest's OneTimeTearDown DropPostgresDb call. Guard on Host so the sqlite path is a
    /// no-op. Wrap in try so a Drop failure (e.g. open connection still draining) does not
    /// mask the underlying test outcome.
    /// </summary>
    private void CleanupPostgresDatabases()
    {
        if (_postgresOptions == null || !_postgresOptions.Host.IsNotNullOrWhiteSpace())
        {
            return;
        }

        try
        {
            PostgresDatabase.Drop(_postgresOptions, MigrationType.Main);
        }
        catch (Exception ex)
        {
            TestContext.Progress.WriteLine($"Drop Main DB failed (non-fatal): {ex.Message}");
        }

        try
        {
            PostgresDatabase.Drop(_postgresOptions, MigrationType.Log);
        }
        catch (Exception ex)
        {
            TestContext.Progress.WriteLine($"Drop Log DB failed (non-fatal): {ex.Message}");
        }
    }
}
