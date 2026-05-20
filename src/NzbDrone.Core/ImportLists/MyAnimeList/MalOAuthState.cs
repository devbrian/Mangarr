using System;
using System.Security.Cryptography;

namespace NzbDrone.Core.ImportLists.MyAnimeList
{
    // Phase 27 Plan 27-04 Task 1 — transient PKCE flow state POCO.
    //
    // Decision (Discretion #2 shape (a) per 27-PATTERNS.md + RESEARCH §Open Question 1):
    // PKCE state persists as a JSON-serialized blob field on the MAL Settings POCO
    // (Settings.PendingPkceState : string). This file declares the in-memory shape
    // serialized into that blob field; lives between startOAuth and getOAuthToken
    // RequestAction calls.
    //
    // Lifecycle:
    //   1. startOAuth: `MalOAuthState.Create()` generates a random `Verifier` (PKCE)
    //      + random `StateNonce` (CSRF), sets `ExpiresAt = now + 10min`, and the
    //      provider serializes the instance into Settings.PendingPkceState.
    //   2. getOAuthToken: the provider deserializes Settings.PendingPkceState into a
    //      `MalOAuthState`, calls `IsValid(presentedState)` to validate CSRF + TTL,
    //      uses `Verifier` for the PKCE code-exchange POST, then NULLS the
    //      Settings.PendingPkceState blob (single-use enforcement).
    //
    // Threat mitigations:
    //   - T-V11 CSRF: 32-byte cryptographically random `StateNonce` generated via
    //     `RandomNumberGenerator.GetBytes(32)` (ASVS V6-compliant CSPRNG). Presented
    //     `state` query parameter must match exactly via `IsValid(presentedState)`.
    //   - T-V11 Replay: `Settings.PendingPkceState = null` on first successful
    //     exchange (caller-enforced — see MalImportList.RequestAction("getOAuthToken")).
    //     10-min TTL via `ExpiresAt` check rejects late callbacks even if the blob
    //     somehow survives.
    //   - T-V6 Crypto-RNG: `RandomNumberGenerator.GetBytes(N)` is the .NET CSPRNG;
    //     NEVER `System.Random` or hash-PRNG.
    //
    // Newtonsoft-serializable: properties are public get/set string and DateTime,
    // matching the project-wide Newtonsoft default serialization contract.
    public class MalOAuthState
    {
        // 32-byte CSPRNG-sourced base64url-encoded nonce. Sent to MAL as the `state`
        // query parameter on the authorize URL; MAL returns it verbatim in the callback
        // URL's `?state=` parameter so Mangarr can verify the callback came from the
        // same flow it started (CSRF defense per RFC 6749 §10.12).
        public string StateNonce { get; set; }

        // 48-byte CSPRNG-sourced base64url-encoded verifier. With MAL's
        // `code_challenge_method=plain` (hashed variant NOT supported — MAL constraint,
        // two independent sources confirm), the verifier is sent BOTH as the
        // `code_challenge` query parameter on the authorize URL AND as the
        // `code_verifier` form field on the token-exchange POST. PKCE-plain is weaker
        // than the hashed variant but is the only method MAL accepts (T-V2 ACCEPT per
        // threat model).
        public string Verifier { get; set; }

        // Wall-clock UTC expiry. `IsValid()` rejects expired states; the caller
        // additionally clears `Settings.PendingPkceState = null` on first successful
        // exchange to enforce single-use.
        public DateTime ExpiresAt { get; set; }

        // CSPRNG-sourced random generator + 10-min TTL. Used by
        // MalImportList.RequestAction("startOAuth") at the top of the PKCE flow.
        public static MalOAuthState Create()
        {
            var nonceBytes = RandomNumberGenerator.GetBytes(MalConstants.StateNonceByteLength);
            var verifierBytes = RandomNumberGenerator.GetBytes(MalConstants.VerifierByteLength);

            return new MalOAuthState
            {
                StateNonce = Base64UrlEncode(nonceBytes),
                Verifier = Base64UrlEncode(verifierBytes),
                ExpiresAt = DateTime.UtcNow.Add(MalConstants.StateTtl)
            };
        }

        // CSRF + TTL validation. Returns true ONLY when the presented `state` matches
        // the persisted StateNonce AND the TTL has not expired. Single-use is enforced
        // by the caller (MalImportList.RequestAction) by clearing PendingPkceState
        // after a successful exchange — this method does NOT mutate state.
        //
        // Equality check uses ordinal comparison — MAL preserves the state value
        // verbatim; case-insensitive comparison would weaken the CSRF defense.
        public bool IsValid(string presentedState)
        {
            if (string.IsNullOrEmpty(presentedState) || string.IsNullOrEmpty(StateNonce))
            {
                return false;
            }

            if (ExpiresAt <= DateTime.UtcNow)
            {
                return false;
            }

            return string.Equals(StateNonce, presentedState, StringComparison.Ordinal);
        }

        // base64url encoding per RFC 7636 §A — base64 with `+` → `-`, `/` → `_`, no
        // padding. Required by MAL's PKCE-plain `code_challenge` query parameter
        // formatting (MAL strips/rejects `=` padding chars; verified against MAL's
        // OAuth API reference).
        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }
    }
}
