using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Automation.Test.TestKit;

// Phase 18 Plan 18-13 -- BL-01 + BL-02 (from 18-REVIEW.md) coverage.
//
// Unit-tier fixture (no [Category("AutomationTest")]; runs in the standard
// unit_test job without a real browser). Verifies three contracts:
//
//   1. write_then_load_roundtrips_content_type -- BL-02: WriteToDiskAsync
//      persists Content-Type + custom response headers; LoadFromDiskAsync
//      replay produces the same MediaType + custom header value.
//   2. sensitive_headers_are_sanitized_on_write -- BL-02 sanitizer: known
//      sensitive header names (Authorization, Cookie, Set-Cookie, X-Api-Key)
//      and their values must never appear in the on-disk JSON.
//   3. invalid_cassette_mode_env_var_does_not_throw -- BL-01: the Enum.TryParse
//      contract that ManagedHttpDispatcher.CreateHttpClient now uses returns
//      false on garbage input (instead of throwing) and true on valid input.
//      Full dispatcher-instantiation exercise would require the proxy + DI
//      surface; this fixture verifies the parse-layer contract that BL-01
//      depends on.
[TestFixture]
public class CassetteHandlerHeadersFixture
{
    private string _cassetteDir;

    [SetUp]
    public void SetUp()
    {
        _cassetteDir = Path.Combine(Path.GetTempPath(), "mangarr-cassette-bl02-" + System.Guid.NewGuid());
        Directory.CreateDirectory(_cassetteDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_cassetteDir))
        {
            Directory.Delete(_cassetteDir, true);
        }
    }

    [Test]
    public async Task write_then_load_roundtrips_content_type()
    {
        // Record pass: inner returns JSON + a custom X-Cache: HIT header.
        var fakeInner = new FakeInnerHandler(
            HttpStatusCode.OK,
            body: "{\"ok\":true}",
            contentType: "application/json",
            customHeaders: new System.Collections.Generic.Dictionary<string, string>
            {
                ["X-Cache"] = "HIT"
            });
        var handler = new CassetteHandler(_cassetteDir, CassetteMode.ReplayOrRecord, sentinelPngPath: string.Empty, fakeInner);
        using var client = new HttpClient(handler);

        var resp1 = await client.GetAsync("https://example.test/data");
        resp1.Content.Headers.ContentType.MediaType.Should().Be("application/json");

        // Replay pass: deliberately give the inner handler a DIFFERENT response
        // (500 + text/plain). If replay correctly loads from disk, the second
        // response's Content-Type should still be application/json and the
        // X-Cache header should still be HIT, proving the bytes came from the
        // cassette and not from the inner handler.
        var fakeInner2 = new FakeInnerHandler(
            HttpStatusCode.InternalServerError,
            body: "should-not-see-this",
            contentType: "text/plain",
            customHeaders: new System.Collections.Generic.Dictionary<string, string>());
        var handler2 = new CassetteHandler(_cassetteDir, CassetteMode.Replay, sentinelPngPath: string.Empty, fakeInner2);
        using var client2 = new HttpClient(handler2);

        var resp2 = await client2.GetAsync("https://example.test/data");
        resp2.Content.Headers.ContentType.Should().NotBeNull();
        resp2.Content.Headers.ContentType.MediaType.Should().Be("application/json");
        resp2.Headers.GetValues("X-Cache").First().Should().Be("HIT");
    }

    [Test]
    public async Task sensitive_headers_are_sanitized_on_write()
    {
        var fakeInner = new FakeInnerHandler(
            HttpStatusCode.OK,
            body: "{\"k\":\"v\"}",
            contentType: "application/json",
            customHeaders: new System.Collections.Generic.Dictionary<string, string>
            {
                ["Authorization"] = "Bearer secret-token",
                ["Cookie"] = "sid=abc",
                ["Set-Cookie"] = "x=y",
                ["X-Api-Key"] = "private-key-123"
            });
        var handler = new CassetteHandler(_cassetteDir, CassetteMode.ReplayOrRecord, sentinelPngPath: string.Empty, fakeInner);
        using var client = new HttpClient(handler);

        await client.GetAsync("https://example.test/secret");

        var cassetteFiles = Directory.GetFiles(_cassetteDir, "*.json");
        cassetteFiles.Should().HaveCount(1, "exactly one cassette file should have been written");

        var content = await File.ReadAllTextAsync(cassetteFiles[0]);

        // Header names should not appear (case-insensitive) in the serialized
        // JSON. The serializer writes both keys and values, so checking values
        // alone is insufficient -- we check both.
        content.Should().NotContain("Bearer secret-token");
        content.Should().NotContain("sid=abc");
        content.Should().NotContain("private-key-123");
        content.Should().NotContain("Authorization");
        content.Should().NotContain("X-Api-Key");

        // Set-Cookie / Cookie name checks (Set-Cookie is a substring of
        // "Cookie", so check the longer one first).
        content.Should().NotContain("Set-Cookie");
        content.Should().NotContain("\"Cookie\"");
    }

    [Test]
    public void invalid_cassette_mode_env_var_does_not_throw()
    {
        // BL-01 contract: ManagedHttpDispatcher.CreateHttpClient calls
        // Enum.TryParse on the env-var value. TryParse must NOT throw on a
        // garbage value, and must return false so the cassette branch
        // silently falls through to the production handler.
        var parseGarbage = System.Enum.TryParse(typeof(CassetteMode), "garbage-not-a-mode", ignoreCase: true, out var _);
        parseGarbage.Should().BeFalse("TryParse on garbage input must return false (not throw, not parse)");

        var parseValid = System.Enum.TryParse(typeof(CassetteMode), "replay", ignoreCase: true, out var modeValue);
        parseValid.Should().BeTrue("TryParse on a valid mode (case-insensitive) must succeed");
        modeValue.Should().Be(CassetteMode.Replay);
    }

    [Test]
    public void reflective_enum_tryparse_matches_managed_dispatcher_resolution()
    {
        // WR-08 (18-REVIEW): ManagedHttpDispatcher.CreateHttpClient resolves
        // Enum.TryParse(Type, string, bool, out object) by reflection across
        // an assembly boundary (Mangarr.Common cannot take a static dep on
        // CassetteMode, which lives in Mangarr.Automation.Test). The prior
        // BL-01 fixture above tested System.Enum.TryParse directly; a future
        // .NET runtime change that reorders or removes overloads would only
        // be caught at integration-test time. This fixture exercises the
        // exact reflective filter shape ManagedHttpDispatcher.cs:222-229
        // builds against, so a runtime-API breakage fails fast in the unit
        // tier.
        var modeEnumType = typeof(CassetteMode);
        var tryParseMethod = typeof(System.Enum).GetMethods()
            .First(m => m.Name == "TryParse"
                        && !m.IsGenericMethodDefinition
                        && m.GetParameters().Length == 4
                        && m.GetParameters()[0].ParameterType == typeof(System.Type)
                        && m.GetParameters()[1].ParameterType == typeof(string)
                        && m.GetParameters()[2].ParameterType == typeof(bool)
                        && m.GetParameters()[3].ParameterType.IsByRef);

        tryParseMethod.Should().NotBeNull("the reflective filter ManagedHttpDispatcher uses must resolve exactly one method on this runtime");

        var args = new object[] { modeEnumType, "Replay", true, null };
        var ok = (bool)tryParseMethod.Invoke(null, args);
        ok.Should().BeTrue("reflective TryParse must succeed on a valid mode value");
        args[3].Should().Be(CassetteMode.Replay, "the out-arg slot must carry the parsed enum value back through Invoke");

        // Reflective invocation must also handle garbage gracefully (matches
        // BL-01 contract — return false, do NOT throw).
        var garbageArgs = new object[] { modeEnumType, "garbage-not-a-mode", true, null };
        var garbageOk = (bool)tryParseMethod.Invoke(null, garbageArgs);
        garbageOk.Should().BeFalse("reflective TryParse on garbage must return false (matches BL-01 fallthrough contract)");
    }

    // Minimal inner handler -- returns a canned HttpResponseMessage with the
    // configured status, body, content-type, and any custom headers (placed
    // on the response.Headers collection via TryAddWithoutValidation so even
    // the normally-request-only Authorization header attaches cleanly for the
    // sanitizer test).
    private class FakeInnerHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        private readonly string _contentType;
        private readonly System.Collections.Generic.Dictionary<string, string> _customHeaders;

        public FakeInnerHandler(
            HttpStatusCode status,
            string body,
            string contentType,
            System.Collections.Generic.Dictionary<string, string> customHeaders)
        {
            _status = status;
            _body = body;
            _contentType = contentType;
            _customHeaders = customHeaders;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken ct)
        {
            var resp = new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body)
            };
            resp.Content.Headers.ContentType = new MediaTypeHeaderValue(_contentType);
            foreach (var h in _customHeaders)
            {
                resp.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }

            return Task.FromResult(resp);
        }
    }
}
