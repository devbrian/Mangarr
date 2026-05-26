using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel.Settings;

namespace NzbDrone.Automation.Test.Tests.Settings.ImportLists;

/// <summary>
/// gh-226 absorption (2026-05-21) — live regression coverage for the two
/// behaviours the deleted Jest fixture
/// <c>frontend/src/Settings/ImportLists/Options/ImportListOptions.test.tsx</c>
/// claimed to pin under Option B (devbrian/Mangarr#226):
///
///   1. <c>advanced_gating</c> — when the Advanced Settings toggle is OFF
///      the entire ImportListOptions FieldSet is hidden (the component
///      returns <c>null</c> from <c>ImportListOptions.tsx:134-136</c>).
///   2. <c>list_sync_tag_picker_conditional</c> — the ListSyncTag picker
///      FormGroup is only rendered when <c>listSyncLevel === 'keepAndTag'</c>
///      (<c>ImportListOptions.tsx:164</c>). Switching the select to any other
///      level must NOT render the picker.
///
/// Pattern κ: zero series-*/episode-*/season-*/add-series- selectors. Uses
/// the Plan 27.1-02 canonical wrapper testid
/// <c>settings-importlists-options</c>.
///
/// Test tier: nightly (no PRSmoke) — toggling advanced settings and walking
/// SELECT controls is a multi-second interaction sequence; PR-smoke covers
/// the rendered shape via <c>ImportListsSettingsTestFixture.options_field_set_is_rendered</c>.
///
/// Note: <c>System.Text.Json</c> is imported explicitly (not fully qualified)
/// because the parent namespace <c>NzbDrone.Automation.Test.Tests</c> contains
/// a <c>System</c> subdirectory, which would shadow the bare-<c>System</c>
/// prefix in a fully-qualified type reference (compiler error CS0234 — the
/// type name <c>Text</c> doesn't exist in <c>NzbDrone.Automation.Test.Tests.System</c>).
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class ImportListOptionsAdvancedGatingFixture : AutomationTest
{
    [OneTimeSetUp]
    public async Task DisableComixAsync()
    {
#pragma warning disable CS0618 // [reason: legacy pre-Phase-33; v1.3 audit per GH #268]
        await new TestKit.TestKit(RootUri, ApiKey, string.Empty).DisableComixIndexerAsync();
#pragma warning restore CS0618
    }

    [Test]
    public async Task options_fieldset_absent_when_advanced_settings_off_then_appears_when_toggled_on()
    {
        // The Advanced Settings toggle is persisted in Zustand
        // (advancedSettingsStore); we don't know its boot state across test
        // runs. So:
        //   (a) Open /settings/importlists. Whatever the boot state, we get
        //       the toggle by testid.
        //   (b) If the options wrapper is visible, click the toggle to hide
        //       it; assert it is now absent.
        //   (c) Click the toggle again; assert the wrapper reappears.
        // This proves both directions of the advanced-settings gate work.
        var page = await new SettingsImportListsPage(Page).OpenAsync(RootUri);
        await Assertions.Expect(page.PageContainer).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        var advancedToggle = Page.GetByTestId("settings-advanced-toggle");
        await Assertions.Expect(advancedToggle).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        var optionsContainer = Page.GetByTestId("settings-importlists-options");

        // Step (a) — read initial state. We don't fail if the container is
        // already absent (advanced OFF on boot); we normalise to a known
        // "advanced is ON, container visible" baseline first.
        var initiallyVisible = await optionsContainer.IsVisibleAsync();
        if (!initiallyVisible)
        {
            await advancedToggle.ClickAsync();
            await Assertions.Expect(optionsContainer).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        }

        // Baseline assertion: ON => container visible.
        await Assertions.Expect(optionsContainer).ToBeVisibleAsync();

        // Step (b) — toggle OFF; the container must vanish.
        await advancedToggle.ClickAsync();
        await Assertions.Expect(optionsContainer).ToHaveCountAsync(0,
            new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });

        // Step (c) — toggle ON again; the container must reappear.
        await advancedToggle.ClickAsync();
        await Assertions.Expect(optionsContainer).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
    }

    [Test]
    public async Task list_sync_tag_picker_renders_only_when_list_sync_level_is_keepAndTag()
    {
        // Two-phase live regression for ImportListOptions.tsx:164 conditional:
        //   `{listSyncLevel.value === 'keepAndTag' ? <FormGroup ... /> : null}`
        //
        // We drive the underlying config via the V5 endpoint
        // (/api/v5/config/importlist) rather than walking the SELECT control
        // by mouse — the SELECT is a custom EnhancedSelectInput whose DOM
        // shape is non-trivial to drive across browsers, and the conditional
        // render condition lives in React state derived from Redux which
        // hydrates from the same endpoint. PUT the config, reload the page,
        // assert the conditional render result.
        using var http = new HttpClient { BaseAddress = new Uri($"{RootUri}/api/v5/") };
        http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Capture the original config so we can restore in finally (no test
        // should leak persistent config state to siblings).
        var originalGet = await http.GetAsync("config/importlist");
        originalGet.IsSuccessStatusCode.Should().BeTrue(
            "GET /api/v5/config/importlist must return 2xx (controller wired by Plan 27.1-01)");

        var originalJson = await originalGet.Content.ReadAsStringAsync();

        try
        {
            // ===== Phase 1: listSyncLevel = 'disabled' — picker MUST be absent =====
            // PUT first to ensure the page hydrates with the disabled value.
            var disabledPayload = new
            {
                id = 1,
                listSyncLevel = "disabled",
                listSyncTag = 0
            };
            var putDisabled = await http.PutAsJsonAsync("config/importlist/1", disabledPayload);
            putDisabled.IsSuccessStatusCode.Should().BeTrue(
                "PUT config/importlist with listSyncLevel='disabled' must succeed (body: {0})",
                await putDisabled.Content.ReadAsStringAsync());

            // Navigate BEFORE EnsureAdvancedSettingsOnAsync — the
            // `settings-advanced-toggle` testid only renders on Settings
            // pages. AutomationTest's [OneTimeSetUp] leaves the page at
            // app root '/' where the toggle is not in the DOM, so calling
            // EnsureAdvancedSettingsOnAsync first would time out when this
            // test runs in isolation (gh-226 PR #236 review — Codex P1).
            await Page.GotoAsync($"{RootUri}/settings/importlists");
            await EnsureAdvancedSettingsOnAsync();

            var optionsContainer = Page.GetByTestId("settings-importlists-options");
            await Assertions.Expect(optionsContainer).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

            // Wait for the Clean Library Level FormLabel to be present — proves
            // Redux hydration completed. The underlying SELECT is rendered as an
            // EnhancedSelectInput (custom <button>) and does NOT expose the
            // [name='listSyncLevel'] attribute the way a native <select> would
            // (live-verified 2026-05-21 on devbrian/Mangarr#226 close-out); the
            // label is the stable hydration marker.
            var cleanLibraryLevelLabel = optionsContainer.GetByText("Clean Library Level");
            await Assertions.Expect(cleanLibraryLevelLabel).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

            // Assertion: listSyncTag picker is NOT in the DOM. listSyncTag is
            // backed by a real <input name='listSyncTag'> via TagInput (not an
            // EnhancedSelectInput), so the [name='listSyncTag'] selector IS
            // correct here.
            var listSyncTagInput = optionsContainer.Locator("[name='listSyncTag']");
            await Assertions.Expect(listSyncTagInput).ToHaveCountAsync(0,
                new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });

            // ===== Phase 2: listSyncLevel = 'keepAndTag' — picker MUST be present =====
            // The picker hydrates from listSyncTag; seed a non-zero value so
            // there's no ambiguity in cell content.
            var keepAndTagPayload = new
            {
                id = 1,
                listSyncLevel = "keepAndTag",
                listSyncTag = 1
            };
            var putKeepAndTag = await http.PutAsJsonAsync("config/importlist/1", keepAndTagPayload);
            putKeepAndTag.IsSuccessStatusCode.Should().BeTrue(
                "PUT config/importlist with listSyncLevel='keepAndTag' must succeed (body: {0})",
                await putKeepAndTag.Content.ReadAsStringAsync());

            await Page.GotoAsync($"{RootUri}/settings/importlists");
            await EnsureAdvancedSettingsOnAsync();

            await Assertions.Expect(optionsContainer).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
            await Assertions.Expect(cleanLibraryLevelLabel).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

            // Assertion: listSyncTag picker IS in the DOM now.
            await Assertions.Expect(listSyncTagInput).ToHaveCountAsync(1,
                new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        }
        finally
        {
            // Restore. The API returns the current row including its id; PUT
            // the same JSON back so siblings see the pre-test state.
            //
            // Defensive parsing (gh-226 PR #236 review — CodeRabbit Major):
            // every property is guarded by ValueKind / TryGet so a future
            // schema drift (null tag, non-numeric id) doesn't throw inside
            // the finally block and leak mutated config to sibling fixtures.
            using var doc = JsonDocument.Parse(originalJson);
            var rootEl = doc.RootElement;

            var restoreId = 1;
            if (rootEl.TryGetProperty("id", out var idEl) &&
                idEl.ValueKind == JsonValueKind.Number &&
                idEl.TryGetInt32(out var parsedId))
            {
                restoreId = parsedId;
            }

            var restoreLevel = "disabled";
            if (rootEl.TryGetProperty("listSyncLevel", out var ll) &&
                ll.ValueKind == JsonValueKind.String)
            {
                restoreLevel = ll.GetString() ?? "disabled";
            }

            var restoreTag = 0;
            if (rootEl.TryGetProperty("listSyncTag", out var lt) &&
                lt.ValueKind == JsonValueKind.Number &&
                lt.TryGetInt32(out var parsedTag))
            {
                restoreTag = parsedTag;
            }

            var restorePayload = new
            {
                id = restoreId,
                listSyncLevel = restoreLevel,
                listSyncTag = restoreTag
            };

            var restoreResp = await http.PutAsJsonAsync(
                $"config/importlist/{restoreId}", restorePayload);
            var restoreBody = await restoreResp.Content.ReadAsStringAsync();
            var failureContext =
                $"restore PUT to config/importlist/{restoreId} must succeed so this test "
                + $"does not leak mutated config (listSyncLevel='{restoreLevel}', "
                + $"listSyncTag={restoreTag}) into sibling fixtures; body: {restoreBody}";
            restoreResp.IsSuccessStatusCode.Should().BeTrue(failureContext);
        }
    }

    private async Task EnsureAdvancedSettingsOnAsync()
    {
        // The Advanced Settings toggle is persisted client-side (Zustand
        // store, localStorage-hydrated). Across distinct browser contexts in
        // automation we may land with advanced=OFF. Click the toggle iff the
        // options wrapper is not yet visible.
        var advancedToggle = Page.GetByTestId("settings-advanced-toggle");
        await Assertions.Expect(advancedToggle).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        var optionsContainer = Page.GetByTestId("settings-importlists-options");
        var visible = await optionsContainer.IsVisibleAsync();
        if (!visible)
        {
            await advancedToggle.ClickAsync();
            await Assertions.Expect(optionsContainer).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        }
    }
}
