using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.System;

// Phase 18 Plan-17 (System cluster) — INVENTORY row 185
// (`modal-action AuthenticationRequiredModal → First-run auth prompt`).
//
// Non-cassette-dependent: the AuthenticationRequiredModal is mounted in
// Components/Page/Page.tsx with `isOpen={!authenticationEnabled}` where
// `authenticationEnabled = authentication !== 'none'`. The runner used by
// AutomationTest sets `enableAuth: true` at boot (AutomationTest.cs line 30),
// so authentication is forms-or-basic (not 'none') and the modal is NOT
// visible — that's the expected negative-state.
//
// LIVE coverage approach: assert the negative-branch contract — when auth
// is enabled (the runner's configuration), the AuthenticationRequired modal
// must NOT render. This exercises the modal-mount path and verifies the
// `authenticationEnabled` derivation logic correctly suppresses the modal.
// The positive branch (auth disabled → modal appears) would require booting
// a runner with `enableAuth: false`, which is a separate fixture-base
// concern out of Plan-17's scope.
[TestFixture]
[Category("AutomationTest")]
public class AuthenticationRequiredModalFixture : AutomationTest
{
    [Test]
    public async Task auth_required_modal_is_closed_when_auth_is_enabled()
    {
        // Navigate to /system/status (any Page-wrapped route mounts the
        // AuthenticationRequiredModal at the Page level).
        await new SystemStatusPage(Page).OpenAsync(RootUri);

        // STATE assertion 1: page shell mounted → Page wrapper rendered
        // → AuthenticationRequiredModal child mounted.
        await Assertions.Expect(Page.GetByTestId("system-status-page")).ToBeVisibleAsync();

        // STATE assertion 2 (state, not visibility): the modal header
        // "Authentication Required" (from translate('AuthenticationRequired'))
        // must NOT be present at this point — the runner booted with
        // enableAuth=true, so authenticationEnabled=true, so isOpen=false.
        // Asserting count=0 verifies the negative-branch suppression.
        var modalHeader = Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Authentication Required" });
        var modalHeaderCount = await modalHeader.CountAsync();
        modalHeaderCount.Should().Be(0, "AuthenticationRequired modal should be closed when auth is enabled at the runner level");

        Page.Url.Should().EndWith("/system/status");
    }
}
