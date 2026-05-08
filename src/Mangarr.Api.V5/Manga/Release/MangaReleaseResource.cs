using NzbDrone.Core.DecisionEngine.Manga;
using NzbDrone.Core.Indexers;
using Sonarr.Http.REST;

namespace Mangarr.Api.V5.Manga.Release
{
    // Sonarr divergence: NEW manga V5 resource per Phase 6 Plan 06-09 — see DIVERGENCE.md.
    // Role-match analog: src/Mangarr.Api.V5/Release/ReleaseResource.cs.
    //
    // Manga sibling preserves: PascalCase POCO + RestResource Id + ToResource() static mapper;
    // Approved + Rejections list (PIPELINE-01 Interactive Search modal contract).
    //
    // Manga sibling diverges from ReleaseResource:
    //   * No QualityModel / Languages enum / SceneMapping (manga has no quality model per
    //     Phase 5 D-04; TranslatedLanguage is BCP-47 string per Phase 3 D-Q4; no scene
    //     numbering for manga).
    //   * Adds MangaId / ChapterIds / ScanlationGroup / TranslatedLanguage as first-class fields.
    //   * Flattens ReleaseInfo + ParsedChapterInfo into a single resource (no nested wrappers).
    //
    // Phase 8 cleanup: collapse with ReleaseResource when Tv/ deletes.
    public class MangaReleaseResource : RestResource
    {
        public string? Guid { get; set; }
        public string? Title { get; set; }
        public string? Indexer { get; set; }
        public int IndexerId { get; set; }
        public long Size { get; set; }
        public int Age { get; set; }
        public double AgeHours { get; set; }
        public double AgeMinutes { get; set; }
        public DateTime PublishDate { get; set; }
        public DownloadProtocol Protocol { get; set; }

        public int? MangaId { get; set; }
        public List<int>? ChapterIds { get; set; }
        public string? ScanlationGroup { get; set; }
        public string? TranslatedLanguage { get; set; }

        public int CustomFormatScore { get; set; }
        public List<string>? CustomFormats { get; set; }

        public bool Approved { get; set; }
        public bool TemporarilyRejected { get; set; }
        public bool Rejected { get; set; }
        public List<string> Rejections { get; set; } = new List<string>();

        public string? CommentUrl { get; set; }
        public string? DownloadUrl { get; set; }
        public string? InfoUrl { get; set; }

        public int ReleaseWeight { get; set; }
    }

    public static class MangaReleaseResourceMapper
    {
        public static MangaReleaseResource ToResource(this MangaDownloadDecision decision, int weight)
        {
            var rc = decision.RemoteChapter;
            var release = rc.Release;

            return new MangaReleaseResource
            {
                Guid = release?.Guid,
                Title = release?.Title,
                Indexer = release?.Indexer,
                IndexerId = release?.IndexerId ?? 0,
                Size = release?.Size ?? 0,
                Age = release?.Age ?? 0,
                AgeHours = release?.AgeHours ?? 0,
                AgeMinutes = release?.AgeMinutes ?? 0,
                PublishDate = release?.PublishDate ?? default,
                Protocol = release?.DownloadProtocol ?? DownloadProtocol.Http,
                MangaId = rc.Manga?.Id,
                ChapterIds = rc.Chapters?.Select(c => c.Id).ToList(),
                ScanlationGroup = release?.ScanlationGroup,
                TranslatedLanguage = release?.TranslatedLanguage,
                CustomFormatScore = rc.CustomFormatScore,
                CustomFormats = rc.CustomFormats?.Select(c => c.Name).ToList(),
                Approved = decision.Approved,
                TemporarilyRejected = decision.TemporarilyRejected,
                Rejected = decision.Rejected,
                Rejections = decision.Rejections.Select(r => r.Reason.ToString() + ": " + (r.Message ?? string.Empty)).ToList(),
                CommentUrl = release?.CommentUrl,
                DownloadUrl = release?.DownloadUrl,
                InfoUrl = release?.InfoUrl,
                ReleaseWeight = weight
            };
        }
    }
}
