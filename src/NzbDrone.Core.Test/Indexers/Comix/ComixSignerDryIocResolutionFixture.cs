using DryIoc;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Comix;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.Comix
{
    /// <summary>
    /// Phase 17 Wave 1 fixture — DryIoc auto-discovery contract (revision iteration 1, W-3).
    /// Asserts the existing <c>RegisterMany</c> convention in
    /// <c>NzbDrone.Common/Composition/Extensions.cs:25-35</c> picks up
    /// <c>ComixPuppeteerSigner</c> as the singleton implementation of
    /// <see cref="IComixSigner"/> WITHOUT requiring any explicit registration in
    /// <c>NzbDrone.Host/Bootstrap.cs</c>.
    ///
    /// <para>
    /// W-3 escape valve: if the registration assertion fails after Wave 1 ships
    /// <c>ComixPuppeteerSigner</c>, the failure is the W-3 signal — file an explicit
    /// <c>container.Register&lt;ComixPuppeteerSigner&gt;(Reuse.Singleton)</c> in a
    /// Bootstrap module (NOT on the impl class) and re-run. Document in
    /// <c>17-02-SUMMARY.md</c>.
    /// </para>
    ///
    /// <para>
    /// NB: We deliberately do NOT call <c>AssemblyLoader.Load</c> (which the production
    /// composition uses) because that helper implicitly probes for
    /// <c>Mangarr.Windows.dll</c> / <c>Mangarr.Mono.dll</c> which are not built into the
    /// test output dir. The W-3 contract being verified here lives entirely in
    /// <c>Mangarr.Core.dll</c>; pulling additional assemblies would also drag in the full
    /// production DI graph (incl. <c>IDatabase</c> resolutions that require an open
    /// SQLite connection — out of scope for a fixture-level reachability check).
    /// </para>
    /// </summary>
    [TestFixture]
    public class ComixSignerDryIocResolutionFixture : CoreTest
    {
        private static IContainer BuildRealContainer()
        {
            // Mirror the production AutoAddServices RegisterMany convention from
            // NzbDrone.Common/Composition/Extensions.cs:25-35, but driven directly off the
            // already-loaded Mangarr.Core assembly.
            var rules = Rules.Default
                .WithMicrosoftDependencyInjectionRules()
                .WithAutoConcreteTypeResolution()
                .WithDefaultReuse(Reuse.Singleton);
            var container = new Container(rules);

            var assemblies = new[]
            {
                typeof(IComixSigner).Assembly,
            };

            container.RegisterMany(
                assemblies,
                serviceTypeCondition: type => type.IsInterface
                    && !string.IsNullOrWhiteSpace(type.FullName)
                    && !type.FullName.StartsWith("System"),
                reuse: Reuse.Singleton);

            container.RegisterMany(
                assemblies,
                serviceTypeCondition: type => !type.IsInterface
                    && !string.IsNullOrWhiteSpace(type.FullName)
                    && !type.FullName.StartsWith("System"),
                reuse: Reuse.Transient);

            // Replace the convention-resolved IIndexerSourceStatusService with a mock so
            // ComixPuppeteerSigner can be Resolve'd at fixture-level without dragging in
            // the IDatabase / SQLite chain (which Mangarr.Host wires explicitly at boot).
            container.RegisterInstance<IIndexerSourceStatusService>(
                new Mock<IIndexerSourceStatusService>().Object,
                ifAlreadyRegistered: IfAlreadyRegistered.Replace);

            // NLog Logger is needed by ComixPuppeteerSigner's ctor; provide one.
            container.RegisterInstance<Logger>(
                LogManager.GetLogger("ComixPuppeteerSigner"),
                ifAlreadyRegistered: IfAlreadyRegistered.Replace);

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

            // Concrete impl name pinned by Phase 17 D-05 / Wave 1 Plan 17-02 Task 1a.
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
            // must be added to a Bootstrap module — NOT to the impl class.
            using var container = BuildRealContainer();
            var asm = typeof(IComixSigner).Assembly;
            var concreteType = asm.GetType("NzbDrone.Core.Indexers.Comix.ComixPuppeteerSigner");
            concreteType.Should().NotBeNull("Plan 17-02 Task 1a must land the concrete class");

            container.IsRegistered(concreteType).Should().BeTrue(
                "RegisterMany convention with Transient reuse on concrete classes per Extensions.cs:33-35");
        }

        [Test]
        public void Env_set_path_replaces_IComixSigner_with_CassettingComixSigner_via_RegisterDelegate()
        {
            // Phase 33 Plan 33-02 / Plan 33-03 LIVE-recording regression lock — the
            // Startup.cs:341-369 env-var-gated swap must use RegisterDelegate (NOT
            // Made.Of-with-lambda) because the closure-captured locals
            // (`comixCassetteDir`, `parsedMode`, `realSigner`) trip
            // DryIoc.Error.UnexpectedExpressionInsteadOfConstantInMadeOf at Configure-time.
            // Originally landed with Made.Of and never caught because the existing
            // ComixSignerDryIocResolutionFixture tests only the env-unset fall-through;
            // surfaced when Plan 33-03 actually drove the SET path against a live backend.
            //
            // This test mirrors the Startup.cs shape verbatim — captured local names,
            // RegisterDelegate signature, IfAlreadyRegistered.Replace, Reuse.Singleton —
            // so future edits to that block keep the registration shape compatible with
            // closure-captured construction.
            using var container = BuildRealContainer();

            var comixCassetteDir = System.IO.Path.GetTempPath();
            var parsedMode = CassetteMode.Replay;
            var realSigner = container.Resolve<ComixPuppeteerSigner>();

            container.RegisterDelegate<IComixSigner>(
                _ => new CassettingComixSigner(comixCassetteDir, parsedMode, realSigner),
                reuse: Reuse.Singleton,
                ifAlreadyRegistered: IfAlreadyRegistered.Replace);

            var first = container.Resolve<IComixSigner>();
            var second = container.Resolve<IComixSigner>();

            first.Should().BeOfType<CassettingComixSigner>(
                "env-SET path must swap the IComixSigner registration to the cassetting layer per Plan 33-02 D-03");
            first.Should().BeSameAs(second,
                "Reuse.Singleton on the replacement must yield same-instance on consecutive Resolve calls");
        }
    }
}
