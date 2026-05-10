using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Indexers.Comix;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Indexers.Comix
{
    /// <summary>
    /// Phase 17 Wave 0 RED fixture — interface/shape contract for the runtime signer.
    /// Reflection-based: does NOT instantiate <c>ComixPuppeteerSigner</c> directly so the
    /// fixture compiles before Wave 1 lands the concrete class. Wave 1 (Plan 17-02 Task 1b)
    /// un-Ignores after authoring <c>ComixPuppeteerSigner.cs</c>; tests then run the
    /// reflection assertions against the loaded type.
    /// </summary>
    [TestFixture]
    [Ignore("WAVE-1-DEP: requires ComixPuppeteerSigner concrete impl from Plan 17-02 Task 1b")]
    public class ComixSignerContractFixture : CoreTest
    {
        private static Type ResolveConcreteType()
        {
            // Look up by full type name from the loaded NzbDrone.Core assembly so we don't
            // take a compile-time dependency on the not-yet-existent class.
            var asm = typeof(IComixSigner).Assembly;
            return asm.GetType("NzbDrone.Core.Indexers.Comix.ComixPuppeteerSigner", throwOnError: false);
        }

        [Test]
        public void Concrete_class_should_exist_in_NzbDrone_Core_assembly()
        {
            var t = ResolveConcreteType();
            t.Should().NotBeNull(
                "Plan 17-02 Task 1b lands NzbDrone.Core.Indexers.Comix.ComixPuppeteerSigner; " +
                "absence here means Wave 1 did not ship the concrete impl.");
        }

        [Test]
        public void Concrete_class_should_implement_IComixSigner()
        {
            var t = ResolveConcreteType();
            t.Should().NotBeNull();
            typeof(IComixSigner).IsAssignableFrom(t).Should().BeTrue(
                "ComixPuppeteerSigner must implement IComixSigner per Phase 17 D-05.");
        }

        [Test]
        public void Concrete_class_should_implement_IDisposable()
        {
            var t = ResolveConcreteType();
            t.Should().NotBeNull();
            typeof(IDisposable).IsAssignableFrom(t).Should().BeTrue(
                "ComixPuppeteerSigner must implement IDisposable for clean Browser teardown per D-05.");
        }

        [Test]
        public void Concrete_class_should_implement_IHandle_of_ApplicationShutdownRequested()
        {
            var t = ResolveConcreteType();
            t.Should().NotBeNull();
            typeof(IHandle<ApplicationShutdownRequested>).IsAssignableFrom(t).Should().BeTrue(
                "ComixPuppeteerSigner must implement IHandle<ApplicationShutdownRequested> per D-05.");
        }

        [Test]
        public void ProxyFetchAsync_signature_returns_Task_of_string()
        {
            var method = typeof(IComixSigner).GetMethod(
                nameof(IComixSigner.ProxyFetchAsync),
                BindingFlags.Public | BindingFlags.Instance);

            method.Should().NotBeNull();
            method.ReturnType.Should().Be(typeof(Task<string>),
                "Phase 17 Q-2 / N-2: signer returns the decoded JSON body, not just a token.");
        }

        [Test]
        public void ProxyFetchAsync_should_accept_apiPath_and_optional_CancellationToken()
        {
            var method = typeof(IComixSigner).GetMethod(nameof(IComixSigner.ProxyFetchAsync));
            method.Should().NotBeNull();

            var parameters = method.GetParameters();
            parameters.Should().HaveCount(2);
            parameters[0].ParameterType.Should().Be(typeof(string));
            parameters[0].Name.Should().Be("apiPath");
            parameters[1].ParameterType.Should().Be(typeof(CancellationToken));
            parameters[1].HasDefaultValue.Should().BeTrue("CancellationToken should be optional per Phase 17 plan");
        }
    }
}
