using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.Flows;

namespace NzbDrone.Automation.Test.Tests.Settings.Profiles;

/// <summary>
/// Phase 30 Plan 30-02 (II2-07) — TranslationProfile EditModal language-reorder round-trip
/// driven through <see cref="SettingsFlow.SetTranslationProfileOrderAsync"/>.
///
/// Pre-Phase-30 the helper carried <c>_ = targetOrdinal;</c> — it discarded its argument
/// and silently passed any test that called it. Plan 30-02 drops the discard and wires
/// the helper through the rank-up / rank-down arrows inside <c>EditTranslationProfileModalContent</c>.
///
/// State assertion (audit-test-assertions.sh Gate 1): after the helper completes, GET
/// <c>/api/v5/translationprofile</c> and verify the language at <c>languages[targetOrdinal]</c>
/// matches what was at index 0 before the reorder — proving the reorder genuinely persisted
/// (a no-op helper would leave the seeded order untouched).
///
/// Reorder semantics (locked in 30-02-PLAN.md Task 1 Part B): for a profile with languages
/// <c>["en", "ja", "zh"]</c>, calling the helper with <c>targetOrdinal: 2</c> moves the
/// FIRST language ("en") down to index 2, producing <c>["ja", "zh", "en"]</c>.
/// </summary>
[TestFixture]
[Category("AutomationTest")]
public class TranslationProfileReorderFixture : AutomationTest
{
    [Test]
    public async Task reorder_persists()
    {
        // Arrange — seed a TranslationProfile with 3 languages so the array index reorder is
        // visible end-to-end (the default Phase-5 baseline profile ships with a single language).
        var apiBase = $"{RootUri}/api/v5/translationprofile";
        var name = $"Plan 30-02 TP Reorder {Guid.NewGuid():N}".Substring(0, 32);

        var createResp = await Page.APIRequest.PostAsync(apiBase, new APIRequestContextOptions
        {
            DataObject = new
            {
                name,
                languages = new[] { "en", "ja", "zh" },
                allowLanguagesNotInProfile = false,
                upgradeAllowed = true
            }
        });
        createResp.Status.Should().BeInRange(
            200,
            299,
            $"create should succeed; body: {await createResp.TextAsync()}");
        var profileId = (await createResp.JsonAsync())!.Value.GetProperty("id").GetInt32();

        // Capture the pre-reorder language at index 0 — what we expect to find at the target
        // ordinal after the helper drives the down-arrow.
        var preLanguages = await ReadLanguagesAsync(apiBase, profileId);
        preLanguages.Should().Equal(
            new[] { "en", "ja", "zh" },
            "sanity: the seeded shape is the assumed pre-reorder shape");
        var movedLanguage = preLanguages[0];
        const int targetOrdinal = 2;

        // Act — invoke the helper. Pre-Plan-30-02 the helper discarded targetOrdinal and the
        // post-state would still equal preLanguages (vacuous PASS). Plan 30-02 honors it.
        await SettingsFlow.SetTranslationProfileOrderAsync(Page, RootUri, profileId, targetOrdinal);

        // Assert — STATE assertion via GET. The moved language now sits at index targetOrdinal,
        // and the remaining languages shifted up by one.
        var postLanguages = await ReadLanguagesAsync(apiBase, profileId);
        postLanguages.Should().HaveCount(
            preLanguages.Count,
            "reorder does not add or drop languages");
        postLanguages[targetOrdinal].Should().Be(
            movedLanguage,
            $"the language originally at index 0 ({movedLanguage}) should be at index {targetOrdinal} after reorder");
        postLanguages.Should().NotEqual(
            preLanguages,
            "post-reorder list must differ from the seeded shape — otherwise the helper is still a no-op");
    }

    private async Task<List<string>> ReadLanguagesAsync(string apiBase, int profileId)
    {
        var resp = await Page.APIRequest.GetAsync($"{apiBase}/{profileId}");
        resp.Status.Should().Be(
            200,
            $"GET /api/v5/translationprofile/{profileId} should succeed; body: {await resp.TextAsync()}");

        var doc = (await resp.JsonAsync())!.Value;
        var langs = new List<string>();
        foreach (var element in doc.GetProperty("languages").EnumerateArray())
        {
            langs.Add(element.GetString()!);
        }

        return langs;
    }
}
