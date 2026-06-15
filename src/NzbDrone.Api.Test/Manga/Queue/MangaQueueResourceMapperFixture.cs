#nullable enable
using FluentAssertions;
using Mangarr.Api.V5.Manga.Queue;
using NUnit.Framework;
using NzbDrone.Core.Queue.Manga;
using NzbDrone.Test.Common;

namespace NzbDrone.Api.Test.Manga.Queue
{
    // GH #307 regression guard. MangaQueueItem populates Status / TrackedDownloadStatus /
    // TrackedDownloadState from enum `.ToString()` (PascalCase), but the inherited Sonarr frontend
    // (QueueStatus.tsx + typings/Queue.ts) keys off camelCase string literals ('completed' /
    // 'warning' / 'importPending'). STJ only camelCases enum-TYPED fields — these are plain strings,
    // so the mapper must camelCase them or every FE `=== 'lowerCase'` test fails (wrong icon, no
    // progress bar, no status-message tooltip — the exact symptom in issue #307).
    [TestFixture]
    public class MangaQueueResourceMapperFixture : TestBase
    {
        [TestCase("Completed", "completed")]
        [TestCase("Downloading", "downloading")]
        [TestCase("Paused", "paused")]
        public void ToResource_camelCases_status(string modelStatus, string expected)
        {
            var resource = new MangaQueueItem { Status = modelStatus }.ToResource();

            resource!.Status.Should().Be(expected);
        }

        [TestCase("Warning", "warning")]
        [TestCase("Ok", "ok")]
        [TestCase("Error", "error")]
        public void ToResource_camelCases_trackedDownloadStatus(string modelStatus, string expected)
        {
            var resource = new MangaQueueItem { TrackedDownloadStatus = modelStatus }.ToResource();

            resource!.TrackedDownloadStatus.Should().Be(expected);
        }

        [TestCase("ImportPending", "importPending")]
        [TestCase("ImportBlocked", "importBlocked")]
        [TestCase("Downloading", "downloading")]
        [TestCase("FailedPending", "failedPending")]
        public void ToResource_camelCases_trackedDownloadState(string modelState, string expected)
        {
            var resource = new MangaQueueItem { TrackedDownloadState = modelState }.ToResource();

            resource!.TrackedDownloadState.Should().Be(expected);
        }

        [Test]
        public void ToResource_leaves_null_enum_strings_null()
        {
            var resource = new MangaQueueItem
            {
                Status = null,
                TrackedDownloadStatus = null,
                TrackedDownloadState = null
            }.ToResource();

            resource!.Status.Should().BeNull();
            resource.TrackedDownloadStatus.Should().BeNull();
            resource.TrackedDownloadState.Should().BeNull();
        }

        [Test]
        public void ToResource_carries_SourceKey_verbatim()
        {
            // SourceKey is a plain string (the gateway SourceKey from ReleaseInfo.Source) — it
            // serializes verbatim and must NOT be camelCased by FirstCharToLower (that helper is
            // only for the enum-ToString status fields per GH #307).
            var resource = new MangaQueueItem { SourceKey = "comix.to" }.ToResource();

            resource!.SourceKey.Should().Be("comix.to");
        }

        [Test]
        public void ToResource_leaves_null_SourceKey_null()
        {
            var resource = new MangaQueueItem { SourceKey = null }.ToResource();

            resource!.SourceKey.Should().BeNull();
        }
    }
}
