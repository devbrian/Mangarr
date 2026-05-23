using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Model;
using NzbDrone.Common.Processes;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Update;
using NzbDrone.Core.Update.Commands;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.UpdateTests
{
    [TestFixture]
    public class UpdateServiceFixture : CoreTest<InstallUpdateService>
    {
        private string _sandboxFolder;

        private UpdatePackage _updatePackage;

        [SetUp]
        public void Setup()
        {
            // Sonarr divergence: Phase 21 D-07 reset MANGARR_MAJOR_VERSION from 10 → 1. The
            // upstream Sonarr fixture used a hardcoded `2.0.0.0` (always a non-major bump when
            // current was 10). With Mangarr's reset, `2 > 1` flips InstallUpdateService.cs:246's
            // "this is a major update — refuse" branch true, short-circuiting every test in this
            // fixture via early `return null`. Construct the test update package's version with
            // the current major so it always represents a same-major upgrade — robust across
            // future major bumps without re-editing.
            var newerVersion = new Version(BuildInfo.Version.Major, BuildInfo.Version.Minor, BuildInfo.Version.Build, BuildInfo.Version.Revision + 1);

            if (OsInfo.IsLinux)
            {
                _updatePackage = new UpdatePackage
                {
                    FileName = $"NzbDrone.develop.{newerVersion}.tar.gz",
                    Url = "http://download.sonarr.tv/v2/develop/mono/NzbDrone.develop.tar.gz",
                    Version = newerVersion
                };
            }
            else
            {
                _updatePackage = new UpdatePackage
                {
                    FileName = $"NzbDrone.develop.{newerVersion}.zip",
                    Url = "http://download.sonarr.tv/v2/develop/windows/NzbDrone.develop.zip",
                    Version = newerVersion
                };
            }

            Mocker.GetMock<IAppFolderInfo>().SetupGet(c => c.TempFolder).Returns(TempFolder);
            Mocker.GetMock<IAppFolderInfo>().SetupGet(c => c.StartUpFolder).Returns(@"C:\Mangarr".AsOsAgnostic);
            Mocker.GetMock<IAppFolderInfo>().SetupGet(c => c.AppDataFolder).Returns(@"C:\ProgramData\Mangarr".AsOsAgnostic);

            Mocker.GetMock<ICheckUpdateService>().Setup(c => c.AvailableUpdate()).Returns(_updatePackage);
            Mocker.GetMock<IVerifyUpdates>().Setup(c => c.Verify(It.IsAny<UpdatePackage>(), It.IsAny<string>())).Returns(true);

            Mocker.GetMock<IProcessProvider>().Setup(c => c.GetCurrentProcess()).Returns(new ProcessInfo { Id = 12 });
            Mocker.GetMock<IRuntimeInfo>().Setup(c => c.ExecutingApplication).Returns(@"C:\Test\Mangarr.exe");

            Mocker.GetMock<IConfigFileProvider>()
                  .SetupGet(s => s.UpdateAutomatically)
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(c => c.FolderWritable(It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.Is<string>(s => s.EndsWith("Mangarr.Update".ProcessNameToExe()))))
                  .Returns(true);

            _sandboxFolder = Mocker.GetMock<IAppFolderInfo>().Object.GetUpdateSandboxFolder();
        }

        private void GivenInstallScript(string path)
        {
            Mocker.GetMock<IConfigFileProvider>()
                  .SetupGet(s => s.UpdateMechanism)
                  .Returns(UpdateMechanism.Script);

            Mocker.GetMock<IConfigFileProvider>()
                  .SetupGet(s => s.UpdateScriptPath)
                  .Returns(path);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(path, StringComparison.Ordinal))
                  .Returns(true);
        }

        [Test]
        public void should_delete_sandbox_before_update_if_folder_exists()
        {
            Mocker.GetMock<IDiskProvider>().Setup(c => c.FolderExists(_sandboxFolder)).Returns(true);

            Subject.Execute(new ApplicationUpdateCommand());

            Mocker.GetMock<IDiskProvider>().Verify(c => c.DeleteFolder(_sandboxFolder, true));
        }

        [Test]
        public void should_not_delete_sandbox_before_update_if_folder_doesnt_exists()
        {
            Mocker.GetMock<IDiskProvider>().Setup(c => c.FolderExists(_sandboxFolder)).Returns(false);

            Subject.Execute(new ApplicationUpdateCommand());

            Mocker.GetMock<IDiskProvider>().Verify(c => c.DeleteFolder(_sandboxFolder, true), Times.Never());
        }

        [Test]
        public void Should_download_update_package()
        {
            var updateArchive = Path.Combine(_sandboxFolder, _updatePackage.FileName);

            Subject.Execute(new ApplicationUpdateCommand());

            Mocker.GetMock<IHttpClient>().Verify(c => c.DownloadFile(_updatePackage.Url, updateArchive));
        }

        [Test]
        public void Should_extract_update_package()
        {
            var updateArchive = Path.Combine(_sandboxFolder, _updatePackage.FileName);

            Subject.Execute(new ApplicationUpdateCommand());

            Mocker.GetMock<IArchiveService>().Verify(c => c.Extract(updateArchive, _sandboxFolder));
        }

        [Test]
        public void Should_copy_update_client_to_root_of_sandbox()
        {
            var updateClientFolder = Mocker.GetMock<IAppFolderInfo>().Object.GetUpdateClientFolder();

            Subject.Execute(new ApplicationUpdateCommand());

            Mocker.GetMock<IDiskTransferService>()
                  .Verify(c => c.TransferFolder(updateClientFolder, _sandboxFolder, TransferMode.Move));
        }

        [Test]
        public void should_start_update_client_if_updater_exists()
        {
            Subject.Execute(new ApplicationUpdateCommand());

            Mocker.GetMock<IProcessProvider>()
                .Verify(c => c.Start(It.IsAny<string>(), It.Is<string>(s => s.StartsWith("12")), null, null, null), Times.Once());
        }

        [Test]
        public void should_return_with_warning_if_updater_doesnt_exists()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.Is<string>(s => s.EndsWith("Mangarr.Update".ProcessNameToExe()))))
                  .Returns(false);

            Subject.Execute(new ApplicationUpdateCommand());

            Mocker.GetMock<IProcessProvider>()
                .Verify(c => c.Start(It.IsAny<string>(), It.IsAny<string>(), null, null, null), Times.Never());

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_return_without_error_or_warnings_when_no_updates_are_available()
        {
            Mocker.GetMock<ICheckUpdateService>().Setup(c => c.AvailableUpdate()).Returns<UpdatePackage>(null);

            Subject.Execute(new ApplicationUpdateCommand());

            ExceptionVerification.AssertNoUnexpectedLogs();
        }

        [Test]
        public void should_not_extract_if_verification_fails()
        {
            Mocker.GetMock<IVerifyUpdates>().Setup(c => c.Verify(It.IsAny<UpdatePackage>(), It.IsAny<string>())).Returns(false);

            Assert.Throws<CommandFailedException>(() => Subject.Execute(new ApplicationUpdateCommand()));

            Mocker.GetMock<IArchiveService>().Verify(v => v.Extract(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        [Platform(Exclude = "Win")]
        public void should_run_script_if_configured()
        {
            const string scriptPath = "/tmp/nzbdrone/update.sh";

            GivenInstallScript(scriptPath);

            Subject.Execute(new ApplicationUpdateCommand());

            Mocker.GetMock<IProcessProvider>().Verify(v => v.Start(scriptPath, It.IsAny<string>(), null, null, null), Times.Once());
        }

        [Test]
        [Platform(Exclude = "Win")]
        public void should_throw_if_script_is_not_set()
        {
            const string scriptPath = "/tmp/nzbdrone/update.sh";

            GivenInstallScript("");

            Assert.Throws<CommandFailedException>(() => Subject.Execute(new ApplicationUpdateCommand()));

            ExceptionVerification.ExpectedErrors(1);
            Mocker.GetMock<IProcessProvider>().Verify(v => v.Start(scriptPath, It.IsAny<string>(), null, null, null), Times.Never());
        }

        [Test]
        [Platform(Exclude = "Win")]
        public void should_throw_if_script_is_null()
        {
            const string scriptPath = "/tmp/nzbdrone/update.sh";

            GivenInstallScript(null);

            Assert.Throws<CommandFailedException>(() => Subject.Execute(new ApplicationUpdateCommand()));

            ExceptionVerification.ExpectedErrors(1);
            Mocker.GetMock<IProcessProvider>().Verify(v => v.Start(scriptPath, It.IsAny<string>(), null, null, null), Times.Never());
        }

        [Test]
        [Platform(Exclude = "Win")]
        public void should_throw_if_script_path_does_not_exist()
        {
            const string scriptPath = "/tmp/nzbdrone/update.sh";

            GivenInstallScript(scriptPath);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(scriptPath, StringComparison.Ordinal))
                  .Returns(false);

            Assert.Throws<CommandFailedException>(() => Subject.Execute(new ApplicationUpdateCommand()));

            ExceptionVerification.ExpectedErrors(1);
            Mocker.GetMock<IProcessProvider>().Verify(v => v.Start(scriptPath, It.IsAny<string>(), null, null, null), Times.Never());
        }

        [Test]
        public void Should_download_and_extract_to_temp_folder()
        {
            // Phase 29 DIST2-04 rewrite (Plan 29-02 Task 3, D-05..D-08): cassette-replay
            // against an in-tree synthetic tarball. Drops [Ignore("TODO fix")] +
            // UseRealHttp() per D-05. The Mocker IHttpClient.DownloadFile callback
            // copies Files/Update/synthetic-mangarr.tar.gz to the destination path
            // so the REAL ArchiveService can extract a real .tar.gz at runtime.
            //
            // R-3 audit: post-Phase-15 rebrand, the release-archive top-level subdir is
            // 'Mangarr/' not 'NzbDrone/' — confirmed empirically against
            // .github/actions/package/package.sh line 57
            // (`tar -zcf "./$artifactsFolder/$archiveName.tar.gz" -C $folderName Mangarr`).
            // The synthetic tarball at Files/Update/synthetic-mangarr.tar.gz mirrors this
            // layout; assertion below uses the matching subdir name.
            // In-tree fixture-file pattern per MangaDexParserFixture.cs:35 — bare relative
            // path; the .csproj <None Update="Files\**\*.*"> glob auto-copies to the test
            // output dir, which is the CWD at test runtime.
            var syntheticTarballPath = Path.Combine("Files", "Update", "synthetic-mangarr.tar.gz");

            // Force a .tar.gz extension on the package filename so ArchiveService.Extract
            // dispatches to ExtractTgz on Windows too (Setup() defaults to .zip on Win).
            _updatePackage.FileName = $"Mangarr.develop.{_updatePackage.Version}.linux-x64.tar.gz";

            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.DownloadFile(It.IsAny<string>(), It.IsAny<string>()))
                  .Callback<string, string>((url, dest) =>
                  {
                      // Real HttpClient.DownloadFile creates parent dirs on demand; the
                      // mock callback must mirror that contract for InstallUpdateService's
                      // packageDestination path (the sandbox folder may not yet exist
                      // when DownloadFile is invoked).
                      Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                      File.Copy(syntheticTarballPath, dest, overwrite: true);
                  });

            var updateSubFolder = new DirectoryInfo(Mocker.GetMock<IAppFolderInfo>().Object.GetUpdateSandboxFolder());

            updateSubFolder.Exists.Should().BeFalse();

            Mocker.SetConstant<IArchiveService>(Mocker.Resolve<ArchiveService>());

            Subject.Execute(new ApplicationUpdateCommand());

            updateSubFolder.Refresh();

            updateSubFolder.Exists.Should().BeTrue();
            updateSubFolder.GetDirectories("Mangarr").Should().HaveCount(1);
            updateSubFolder.GetDirectories().Should().HaveCount(1);
            updateSubFolder.GetFiles().Should().NotBeEmpty();
        }

        [Test]
        public void should_log_error_when_app_data_is_child_of_startup_folder()
        {
            Mocker.GetMock<IAppFolderInfo>().SetupGet(c => c.StartUpFolder).Returns(@"C:\NzbDrone".AsOsAgnostic);
            Mocker.GetMock<IAppFolderInfo>().SetupGet(c => c.AppDataFolder).Returns(@"C:\NzbDrone\AppData".AsOsAgnostic);

            Assert.Throws<CommandFailedException>(() => Subject.Execute(new ApplicationUpdateCommand()));
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_log_error_when_app_data_is_same_as_startup_folder()
        {
            Mocker.GetMock<IAppFolderInfo>().SetupGet(c => c.StartUpFolder).Returns(@"C:\NzbDrone".AsOsAgnostic);
            Mocker.GetMock<IAppFolderInfo>().SetupGet(c => c.AppDataFolder).Returns(@"C:\NzbDrone".AsOsAgnostic);

            Assert.Throws<CommandFailedException>(() => Subject.Execute(new ApplicationUpdateCommand()));
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_log_error_when_startup_folder_is_not_writable()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(c => c.FolderWritable(It.IsAny<string>()))
                  .Returns(false);

            var updateArchive = Path.Combine(_sandboxFolder, _updatePackage.FileName);

            Assert.Throws<CommandFailedException>(() => Subject.Execute(new ApplicationUpdateCommand()));

            Mocker.GetMock<IHttpClient>().Verify(c => c.DownloadFile(_updatePackage.Url, updateArchive), Times.Never());
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_log_when_install_cannot_be_started()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(c => c.FolderWritable(It.IsAny<string>()))
                  .Returns(false);

            var updateArchive = Path.Combine(_sandboxFolder, _updatePackage.FileName);

            Assert.Throws<CommandFailedException>(() => Subject.Execute(new ApplicationUpdateCommand()));

            Mocker.GetMock<IHttpClient>().Verify(c => c.DownloadFile(_updatePackage.Url, updateArchive), Times.Never());
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_switch_to_branch_specified_in_updatepackage()
        {
            _updatePackage.Branch = "fake";

            Subject.Execute(new ApplicationUpdateCommand());

            Mocker.GetMock<IConfigFileProvider>()
                  .Verify(v => v.SaveConfigDictionary(It.Is<Dictionary<string, object>>(d => d.ContainsKey("Branch") && (string)d["Branch"] == "fake")), Times.Once());
        }

        [Test]
        public void should_not_update_with_built_in_updater_inside_docker_container()
        {
            Mocker.GetMock<IDeploymentInfoProvider>().Setup(x => x.PackageUpdateMechanism).Returns(UpdateMechanism.Docker);

            Subject.Execute(new ApplicationUpdateCommand());

            Mocker.GetMock<IProcessProvider>()
                .Verify(c => c.Start(It.IsAny<string>(), It.Is<string>(s => s.StartsWith("12")), null, null, null), Times.Never());
        }

        [Test]
        public void should_not_update_with_built_in_updater_when_external_updater_is_configured()
        {
            Mocker.GetMock<IDeploymentInfoProvider>().Setup(x => x.IsExternalUpdateMechanism).Returns(true);

            Subject.Execute(new ApplicationUpdateCommand());

            Mocker.GetMock<IProcessProvider>()
                .Verify(c => c.Start(It.IsAny<string>(), It.Is<string>(s => s.StartsWith("12")), null, null, null), Times.Never());
        }

        [TearDown]
        public void TearDown()
        {
            ExceptionVerification.IgnoreErrors();
        }
    }
}
