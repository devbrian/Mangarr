using System;
using System.Collections.Generic;

namespace NzbDrone.Core.MediaFiles
{
    // Sonarr divergence: Phase 15 Plan 15-10 cascade absorption —
    // Quality-keyed map stripped per Plan 15-03 Quality DELETE. Extensions list preserved
    // for FileExtensions (extension-collision detection) + V5 MediaManagementSettings
    // (UserRejectedExtensions overlap detection).
    public static class MediaFileExtensions
    {
        private static readonly HashSet<string> _extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".webm",
            ".m4v", ".3gp", ".nsv", ".ty", ".strm", ".rm", ".rmvb", ".m3u", ".ifo",
            ".mov", ".qt", ".divx", ".xvid", ".bivx", ".nrg", ".pva", ".wmv", ".asf",
            ".asx", ".ogm", ".ogv", ".m2v", ".avi", ".bin", ".dat", ".dvr-ms", ".mpg",
            ".mpeg", ".mp4", ".avc", ".vp3", ".svq3", ".nuv", ".viv", ".dv", ".fli",
            ".flv", ".wpl",
            ".img", ".iso", ".vob",
            ".mkv", ".ts", ".wtv",
            ".m2ts"
        };

        public static HashSet<string> Extensions => new HashSet<string>(_extensions, StringComparer.OrdinalIgnoreCase);
        public static HashSet<string> DiskExtensions => new HashSet<string>(new[] { ".img", ".iso", ".vob" }, StringComparer.OrdinalIgnoreCase);
    }
}
