using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Download.Clients.InProcess;

namespace NzbDrone.Core.Test.Download.Clients.InProcess
{
    // gh170 (WR-01) — contract test that the InProcessImageDownloadClient backend
    // type name, when run through the suffix-strip rule applied by
    //   frontend/src/Settings/DownloadClients/DownloadClients/AddDownloadClientItem.tsx
    //     lines 48-52
    //       implementation
    //         .replace(/Indexer$/i, '')
    //         .replace(/DownloadClient$/i, '')
    //         .replace(/Notification$/i, '')
    //         .toLowerCase();
    // produces the slug that
    //   src/NzbDrone.Automation.Test/Flows/SettingsProviderFlow.cs::OpenPickerAndSelectAsync
    // expects callers to pass as `implementationSlug` for the in-process downloader
    // (`add-downloadclient-{slug}` testid).
    //
    // Failure mode this guards against: a rename of the backend type OR a change to
    // the frontend strip rule silently drifts the data-testid that Playwright
    // fixtures click on. With no fixture today, drift would surface only as a flaky
    // settings smoke run hours/days later. This fixture fast-fails at unit-test time.
    [TestFixture]
    public class InProcessImageDownloadClientSlugFixture
    {
        // The slug Playwright fixtures pass to
        // SettingsProviderFlow.OpenPickerAndSelectAsync(page, "downloadclient", "inprocessimage").
        // Must equal what AddDownloadClientItem.tsx renders into
        // data-testid="add-downloadclient-{slug}" for this implementation.
        private const string ExpectedSlug = "inprocessimage";

        [Test]
        public void InProcessImageDownloadClient_type_name_derives_to_expected_picker_slug()
        {
            var derived = DeriveAddDownloadClientItemSlug(typeof(InProcessImageDownloadClient).Name);

            derived.Should().Be(ExpectedSlug);
        }

        // Mirrors AddDownloadClientItem.tsx lines 48-52 verbatim. Keep this method in
        // sync with the .tsx — if either side changes, the other must follow.
        private static string DeriveAddDownloadClientItemSlug(string implementation)
        {
            var stripped = Regex.Replace(implementation, "Indexer$", string.Empty, RegexOptions.IgnoreCase);
            stripped = Regex.Replace(stripped, "DownloadClient$", string.Empty, RegexOptions.IgnoreCase);
            stripped = Regex.Replace(stripped, "Notification$", string.Empty, RegexOptions.IgnoreCase);
            return stripped.ToLowerInvariant();
        }
    }
}
