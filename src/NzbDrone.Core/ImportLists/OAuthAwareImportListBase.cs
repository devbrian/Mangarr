using System;
using System.Collections.Concurrent;
using System.Threading;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser.Manga;

namespace NzbDrone.Core.ImportLists
{
    // Phase 27 Plan 27-01 — intermediate abstract base between HttpImportListBase<TSettings>
    // and the 3 OAuth-bearing provider plugins (MangaDex follows / AniList list / MAL list).
    //
    // Pattern source (verbatim refresh-shape gospel):
    //   .planning/reference/sonarr-vertical-slices/notifications-extra/Trakt/Trakt.cs:127-163
    // with two Phase-27 adjustments inserted per Plan 27-01 27-PATTERNS.md Pattern A:
    //   1. D-05: per-ImportList SemaphoreSlim wraps RefreshToken() to serialize concurrent
    //      refresh attempts on the same Definition.Id (Pitfall 9 — MAL's refresh-token
    //      rotation endpoint revokes the prior token on a concurrent grant, surfacing as
    //      a 400 invalid_grant cascade across all ImportListSync invocations sharing the
    //      same ImportList). The semaphore registry is keyed on Definition.Id (int FK)
    //      so two distinct ImportLists with the same provider implementation refresh in
    //      parallel without contention.
    //   2. Peer-flow defense: after acquiring the semaphore, RefreshTokenIfNecessary
    //      re-checks Settings.Expires — a peer thread may have refreshed the token while
    //      this thread was blocked on Wait(). The re-check collapses the 7-redundant-call
    //      worst case in the parallel-8 fixture (Plan 27-01 Task 2) into a single live
    //      refresh.
    //
    // D-06: providers implement Trakt-canonical RequestAction("startOAuth", query) +
    // RequestAction("getOAuthToken", query) / RequestAction("getAuthPin", query) per
    // D-07/D-08/D-09; that surface lives on the concrete provider class (RequestAction is
    // already virtual on ImportListBase), NOT here. This base only owns the refresh
    // template + semaphore registry that all 3 providers share.
    //
    // D-01: TSettings carries IOAuthImportListSettings (plain AccessToken/RefreshToken/
    // Expires/AuthUser properties); no encryption-at-rest infrastructure is introduced.
    public abstract class OAuthAwareImportListBase<TSettings> : HttpImportListBase<TSettings>
        where TSettings : ImportListSettingsBase<TSettings>, IOAuthImportListSettings, new()
    {
        // Process-lifetime semaphore registry, keyed on ImportListDefinition.Id. A new
        // ConcurrentDictionary entry is created on first refresh attempt per ImportList
        // (GetOrAdd guarantees a single SemaphoreSlim instance under contention). The
        // entry is never explicitly removed — at v1.1 scale the count is bounded by the
        // number of ImportLists a user has configured (typically <10), so the leak is
        // a non-concern. If a future surge in ImportList count materializes, an
        // IHandle<ProviderDeletedEvent<IMangaImportList>> handler can clean up on
        // provider deletion (Phase 26 ImportListExclusionService precedent).
        private static readonly ConcurrentDictionary<int, SemaphoreSlim> _refreshLocks = new();

        protected virtual TimeSpan TokenRefreshLookahead => TimeSpan.FromMinutes(5);

        protected OAuthAwareImportListBase(
            IHttpClient httpClient,
            IImportListStatusService importListStatusService,
            IConfigService configService,
            IMangaParsingService parsingService,
            ILocalizationService localizationService,
            Logger logger)
            : base(httpClient, importListStatusService, configService, parsingService, localizationService, logger)
        {
        }

        // Trakt.cs:127-133 verbatim refresh template, augmented with the D-05 semaphore
        // wrap + peer-flow re-check. Sonarr's Trakt notifier calls this at the top of
        // every API entry point (OnDownload / OnImportComplete / etc.); Phase 27
        // providers call it from Fetch() (see overridden Fetch() below) and from any
        // additional API touchpoints (e.g., TestConnection on provider-specific overrides).
        protected void RefreshTokenIfNecessary()
        {
            // Common-path early-return — no semaphore acquire needed when the token is
            // still well within its valid window.
            if (Settings.Expires >= DateTime.UtcNow.Add(TokenRefreshLookahead))
            {
                return;
            }

            var lockObj = _refreshLocks.GetOrAdd(Definition.Id, _ => new SemaphoreSlim(1, 1));
            lockObj.Wait();
            try
            {
                // Peer-flow defense: a sibling thread may have completed the refresh
                // while we were blocked on Wait(). Re-check Settings.Expires inside
                // the lock so the second-through-Nth concurrent callers no-op instead
                // of issuing redundant refresh requests (Pitfall 9 — concurrent refresh
                // on the same Definition.Id revokes the prior token on MAL).
                if (Settings.Expires >= DateTime.UtcNow.Add(TokenRefreshLookahead))
                {
                    return;
                }

                RefreshToken();
            }
            finally
            {
                lockObj.Release();
            }
        }

        // Concrete providers (MangaDex / AniList / MAL) override this with the
        // per-provider refresh-grant HTTP call + Settings persistence per Trakt.cs:135-163
        // pattern. AniList's 1-year JWT means its override is effectively a no-op (logs
        // a "manual re-auth required" line). MAL honors the refresh-rotation null-coalesce
        // (Trakt.cs:151 verbatim: Settings.RefreshToken = token.RefreshToken ?? Settings.RefreshToken).
        protected abstract void RefreshToken();

        // Wrap Fetch() so RefreshTokenIfNecessary runs at the top of every sync cycle
        // (Trakt.cs:38/44/51/57/63/71 pattern — refresh-check is the first thing every
        // API entry point does). The 401-trap fallback (force-expire + retry once) is
        // wired by individual providers in their FetchImportListResponse overrides per
        // RESEARCH §Pattern 2 + Plan 27-04 (MAL's 31-day refresh-rotation can drift if
        // the lookahead misses; the 401-retry is the safety net).
        public override ImportListFetchResult Fetch()
        {
            RefreshTokenIfNecessary();
            return base.Fetch();
        }
    }
}
