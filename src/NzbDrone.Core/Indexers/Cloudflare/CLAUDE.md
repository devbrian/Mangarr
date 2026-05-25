# Indexers/Cloudflare

## Purpose

Generic, **reusable** Cloudflare-challenge clearance seam for HTTP manga-aggregator indexers. Resolves a `cf_clearance` cookie + the matched User-Agent for a target URL by POSTing to a user-configured FlareSolverr/Byparr-API-compatible sidecar, and caches the `(cookie, UA)` pair per-host.

This is a **SEPARATE axis** from comix.to's env-module response-signing. The Phase 17 `IComixSigner` "manga-only, don't generalize" rule (`src/NzbDrone.Core/Indexers/CLAUDE.md`) governs the **signing/decryption** layer only — it does NOT govern CF clearance. CF clearance (passing the "Just a moment" challenge → attaching a `cf_clearance` cookie + matched UA to requests) is **allowed to generalize** because any browser-less aggregator indexer (MangaFire / MangaPark / etc. future ports) will need it (D-09, user direction 2026-05-25: "other indexers will likely need cloudflare bypass as well").

**Absolute Path**: `C:\Users\jones\Desktop\Mangarr\Mangarr\src\NzbDrone.Core\Indexers\Cloudflare`

Shipped Phase 33.2 (v1.2, 2026-05-25). No Sonarr peer — Sonarr never bundled a FlareSolverr-style anti-bot sidecar (fork-divergent; see `DIVERGENCE.md`).

## Key Files

| File | Purpose |
|------|---------|
| `ICloudflareClearanceService.cs` | The seam. `Task<CloudflareClearance> GetClearanceAsync(string targetUrl, CancellationToken ct)`. Auto-discovered as a `Reuse.Singleton` via the existing `RegisterMany` convention. Throws `CloudflareSolverNotConfiguredException` (distinct — D-07) when no solver URL is set; `CloudflareSolverException` on a non-"ok" solver status or missing `cf_clearance`. |
| `CloudflareClearanceService.cs` | Non-disposable `Reuse.Singleton` impl. Process-lifetime static `HttpClient`; per-host `ConcurrentDictionary` cache with a **20-minute TTL**. `protected virtual ClearanceTtl` + `protected virtual PostSolveAsync` test seams. Reads the global endpoint from `IConfigService.CloudflareSolverUrl`. Logs host + status only (never the cookie value — T-33.2-01/T-33.2-13). |
| `CloudflareClearance.cs` | Immutable value record carrying ONE atomic clearance: the `cf_clearance` cookie value + the EXACT UA that solved the challenge + the solver cookie's verbatim `domain`/`path`/`secure`/`httpOnly` (Pitfall 5) + the cache `ExpiresAt`. |
| `FlareSolverrDto.cs` | FlareSolverr/Byparr `/v1` request + response POCOs (`solution.cookies[]`, `solution.userAgent`, `status`, `message`). |
| `CloudflareSolverException.cs` | `CloudflareSolverException` (solver failure) + `CloudflareSolverNotConfiguredException` (no URL configured — the distinct D-07 health-check / Test-Connection branch). |

## Patterns / Conventions

**The atomic `(cookie, UA)` invariant (Pitfall 1 / Pitfall 4).** A `cf_clearance` cookie is bound to the User-Agent that earned it. Attaching the cookie under a *different* UA gets the request re-challenged. `CloudflareClearance` therefore carries the cookie and the matched UA together as one unit; consumers MUST apply both. Plan 02's Comix injection applies them in the exact order `SetUserAgentAsync` → `SetCookieAsync` → `GoToAsync`.

**Verbatim cookie attributes (Pitfall 5).** The solver cookie's `domain`/`path`/`secure`/`httpOnly` are copied as-is so a downstream consumer can re-apply the cookie faithfully (a `secure`/`httpOnly`/domain mismatch silently drops the cookie).

**Non-disposable singleton + per-host TTL cache (D-03).** One process-lifetime `HttpClient`; the cache is keyed by host so a single solve covers every route on that host until the 20-min TTL lapses. The 20-min default is conservative: the LIVE comix.to `cf_clearance` observed at close-out (2026-05-25) expired ~1 year out, far exceeding 20 min — no TTL tuning needed.

**Reusability is structural, not aspirational (D-09).** `FakeHttpAggregatorClearanceConsumerFixture` exercises a FAKE plain-`IHttpClient` consumer of the seam and references **zero** Comix types (`grep -c Comix == 0` is a standing gate). Comix is the only *production* consumer this phase; the fixture proves a second indexer could consume the seam without touching `Indexers/Comix/`.

## Manga Adaptation Notes

Net-new in Mangarr; nothing to port from Sonarr. The user-facing surface (global solver URL + Test Connection on Settings > Indexers > Options, distinct Health Check) lives in Plan 03 (`HealthCheck/Checks/CloudflareSolverCheck.cs`, `Mangarr.Api.V5/Settings/`, `frontend/src/Settings/Indexers/Options/`). The global URL is persisted via `IConfigService.CloudflareSolverUrl`.

## Cross-References

- [Indexers/CLAUDE.md](../CLAUDE.md) — the `IComixSigner` manga-only rule (signing axis; distinct from this clearance axis)
- [Indexers/Comix/CLAUDE.md](../Comix/CLAUDE.md) — the only production consumer (Phase 33.2 clearance-injection note under Phase 17 Invariants / T-CF-403)
- `DIVERGENCE.md` — Phase 33.2 fork-divergent entry (no Sonarr peer)
- `.planning/phases/33.2-cloudflare-bypass-for-manga-aggregator-indexers/` — CONTEXT (D-01..D-12) / RESEARCH / PLAN / SUMMARY
