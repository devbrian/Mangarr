using System.Collections.Generic;
using DryIoc;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Composition.Extensions;
using NzbDrone.Core.Indexers.Comix;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.Comix
{
    /// <summary>
    /// Phase 17 Wave 0 RED fixture — DryIoc auto-discovery contract
    /// (revision iteration 1, W-3): asserts the existing <c>RegisterMany</c> convention
    /// in <c>NzbDrone.Common/Composition/Extensions.cs:25-35</c> yields singleton-via-interface
    /// AND concrete-class registration for <c>ComixPuppeteerSigner</c> WITHOUT requiring
    /// any explicit registration in <c>NzbDrone.Host/Bootstrap.cs</c>.
    ///
    /// <para>
    /// W-3 escape valve: if either assertion fails after Wave 1 ships
    /// <c>ComixPuppeteerSigner</c>, the failure is the W-3 signal — file an explicit
    /// <c>container.Register&lt;ComixPuppeteerSigner&gt;(Reuse.Singleton)</c> in a
    /// Bootstrap module (NOT on the impl class) and re-run. Document in
    /// <c>17-02-SUMMARY.md</c>.
    /// </para>
    /// </summary>
    [TestFixture]
    [Ignore("WAVE-1-DEP: requires ComixPuppeteerSigner concrete impl from Plan 17-02 Task 1b")]
    public class ComixSignerDryIocResolutionFixture : CoreTest
    {
        private static IContainer BuildRealContainer()
        {
            // Mirror NzbDrone.Host bootstrap rules; load only the Mangarr.Core assembly
            // (the only assembly that contains IComixSigner + ComixPuppeteerSigner).
            var rules = Rules.Default.WithNzbDroneRules();
            var container = new Container(rules);
            container.AutoAddServices(new List<string> { "Mangarr.Core", "Mangarr.Common" });
            return container;
        }

        [Test]
        public void Container_should_resolve_IComixSigner_to_singleton_instance()
        {
            using var container = BuildRealContainer();

            var first = container.Resolve<IComixSigner>();
            var second = container.Resolve<IComixSigner>();

            first.Should().NotBeNull();
            first.Should().BeSameAs(second,
                "Reuse.Singleton convention should yield same-instance on consecutive Resolve calls per Phase 17 D-05");
        }

        [Test]
        public void Resolved_IComixSigner_should_be_ComixPuppeteerSigner()
        {
            using var container = BuildRealContainer();
            var signer = container.Resolve<IComixSigner>();

            // Concrete impl name pinned by Phase 17 D-05 / Wave 1 Plan 17-02 Task 1b.
            signer.GetType().FullName.Should().Be("NzbDrone.Core.Indexers.Comix.ComixPuppeteerSigner");
        }

        [Test]
        public void Container_should_register_ComixPuppeteerSigner_concrete_class()
        {
            // Phase 17 W-3 contract: existing RegisterMany convention yields
            // singleton-via-interface AND concrete-class registration.
            //
            // If this assertion fails after Wave 1 ships ComixPuppeteerSigner, an explicit
            //   container.Register<ComixPuppeteerSigner>(Reuse.Singleton)
            // must be added to a Bootstrap module — NOT to the impl class. The Bootstrap
            // module is the right surface because (a) DryIoc rules live there, (b) future
            // Sonarr-rebase pulls don't need to re-add a per-class attribute, (c) it keeps
            // the impl class free of DI metadata.
            using var container = BuildRealContainer();
            var asm = typeof(IComixSigner).Assembly;
            var concreteType = asm.GetType("NzbDrone.Core.Indexers.Comix.ComixPuppeteerSigner");
            concreteType.Should().NotBeNull("Plan 17-02 Task 1b must land the concrete class");

            container.IsRegistered(concreteType).Should().BeTrue(
                "RegisterMany convention with Transient reuse on concrete classes per Extensions.cs:33-35");
        }
    }
}
