using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Composition;
using NzbDrone.Core.Messaging.Commands;
using Mangarr.Api.V5.Commands;
using Sonarr.Http.REST;
using MangaManualImportCommand = NzbDrone.Core.MediaFiles.MangaImport.Manual.ManualImportCommand;
using TvManualImportCommand = NzbDrone.Core.MediaFiles.EpisodeImport.Manual.ManualImportCommand;

namespace NzbDrone.Api.Test.v5.Commands
{
    // Plan 11-09 fixture — verifies the routing decisions for the ManualImportCommand
    // simple-name collision in CommandTypeResolver.Resolve.
    //
    // Bug context (audit at .planning/phases/08-tv-manga-parity-audit/audit/
    // ManualImportCommand-Tv-vs-ManualImportCommand-Manga.md gap-01):
    // both EpisodeImport.Manual.ManualImportCommand and MangaImport.Manual.ManualImportCommand
    // produce simple Type.Name = "ManualImportCommand". Pre-fix, the CommandController.StartCommand
    // .Single(...) lookup threw 500. Post-fix, the resolver routes via FullName when a contractName
    // is supplied (or via the transitional TV-prefer rule when not).
    //
    // Phase 14 disposition: once Tv/ deletes, the simple-name collision evaporates and the
    // multi-match branches become dead-code-removal-eligible.
    [TestFixture]
    public class CommandControllerAmbiguousNameFixture
    {
        private KnownTypes _knownTypes;

        [SetUp]
        public void SetUp()
        {
            // KnownTypes built from the CLR-loaded NzbDrone.Core assembly — contains
            // both ManualImportCommand classes per the Composition.Extensions
            // .AutoAddServices KnownTypes construction at runtime.
            var coreAssembly = typeof(Command).Assembly;
            _knownTypes = new KnownTypes(coreAssembly.GetTypes().ToList());
        }

        [Test]
        public void should_resolve_unambiguous_command_by_simple_name()
        {
            // Pick a non-colliding command. RefreshSeries is unambiguous in the loaded assembly.
            var resolved = CommandTypeResolver.Resolve(_knownTypes, "RefreshSeries", null);
            resolved.Name.Should().Be("RefreshSeriesCommand");
        }

        [Test]
        public void should_throw_NotFoundException_for_unknown_name()
        {
            Action act = () => CommandTypeResolver.Resolve(_knownTypes, "ThisCommandDoesNotExist", null);
            act.Should().Throw<NotFoundException>();
        }

        [Test]
        public void should_prefer_tv_namespace_when_ambiguous_and_no_contractName()
        {
            // Transitional TV-prefer rule — keeps the existing V5 UI POST flow working unchanged
            // (frontend/src/InteractiveImport/Interactive/InteractiveImportModalContent.tsx:622-628
            // POSTs name: "ManualImport" with no contractName).
            var resolved = CommandTypeResolver.Resolve(_knownTypes, "ManualImport", null);
            resolved.Should().Be<TvManualImportCommand>();
        }

        [Test]
        public void should_route_to_manga_when_contractName_supplied()
        {
            var resolved = CommandTypeResolver.Resolve(
                _knownTypes,
                "ManualImport",
                "NzbDrone.Core.MediaFiles.MangaImport.Manual.ManualImportCommand");
            resolved.Should().Be<MangaManualImportCommand>();
        }

        [Test]
        public void should_route_to_tv_when_explicit_tv_contractName_supplied()
        {
            var resolved = CommandTypeResolver.Resolve(
                _knownTypes,
                "ManualImport",
                "NzbDrone.Core.MediaFiles.EpisodeImport.Manual.ManualImportCommand");
            resolved.Should().Be<TvManualImportCommand>();
        }

        [Test]
        public void should_throw_BadRequestException_for_mismatched_contractName()
        {
            Action act = () => CommandTypeResolver.Resolve(
                _knownTypes,
                "ManualImport",
                "NzbDrone.Core.MediaFiles.MangaImport.Manual.NotARealCommand");
            act.Should().Throw<BadRequestException>();
        }

        [Test]
        public void contractName_is_case_insensitive_match_against_FullName()
        {
            var resolved = CommandTypeResolver.Resolve(
                _knownTypes,
                "ManualImport",
                "nzbdrone.core.mediafiles.mangaimport.manual.manualimportcommand");
            resolved.Should().Be<MangaManualImportCommand>();
        }
    }
}
