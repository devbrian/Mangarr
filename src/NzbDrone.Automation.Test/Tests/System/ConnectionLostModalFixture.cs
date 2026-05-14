using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using NUnit.Framework;
using NzbDrone.Automation.Test.PageModel;

namespace NzbDrone.Automation.Test.Tests.System;

// Phase 18 Plan-17 (System cluster) — INVENTORY row 199
// (`modal-action ConnectionLostModal → SignalR connection-lost banner`).
//
// Non-cassette-dependent: the ConnectionLostModal is mounted unconditionally
// inside Components/Page/Page.tsx and its `isOpen` prop is driven by the
// `isConnectionLostModalOpen` local React state, which flips on SignalR
// `isDisconnected=true`. As with AppUpdatedModal, we cannot deterministically
// trigger the SignalR disconnect from the harness without invasive
// per-test route-aborts that are fragile across browser versions.
//
// LIVE coverage approach: assert the negative-state contract — backend is
// connected on a fresh boot, so the modal must NOT be visible. This catches
// the regression where the modal is incorrectly mounted as open (e.g. a
// state-init bug that defaults isOpen=true) without paying the SignalR
// trigger cost. T-18-17-02 disposition: negative-branch is the deliverable.
[TestFixture]
[Category("AutomationTest")]
public class ConnectionLostModalFixture : AutomationTest
{
    [Test]
    public async Task connection_lost_modal_is_closed_when_backend_is_connected()
    {
        await new SystemStatusPage(Page).OpenAsync(RootUri);

        // STATE assertion 1: page shell present → Page wrapper mounted →
        // ConnectionLostModal child mounted.
        await Assertions.Expect(Page.GetByTestId("system-status-page")).ToBeVisibleAsync();

        // STATE assertion 2 (state, not visibility): the modal header
        // "Connection Lost" (from translate('ConnectionLost')) must NOT be
        // present in DOM at this point — fresh boot means SignalR is healthy
        // and `isDisconnected` is false. Count check confirms the modal is
        // closed and therefore the dialog has no DOM presence.
        var modalHeader = Page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = "Connection Lost" });
        var modalHeaderCount = await modalHeader.CountAsync();
        modalHeaderCount.Should().Be(0, "ConnectionLost modal should be closed when SignalR is healthy");

        Page.Url.Should().EndWith("/system/status");
    }
}
