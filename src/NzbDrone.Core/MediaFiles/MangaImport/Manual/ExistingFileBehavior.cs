namespace NzbDrone.Core.MediaFiles.MangaImport.Manual
{
    /// <summary>
    /// Per-row "On Existing File" behavior per Phase 25 Plan 25-04 D-04 +
    /// v2-02. Skip preserves no-destructive-default safety — the user must
    /// opt in to overwrite a row. Serializes as camelCase strings
    /// ("skip" / "replace") via the global STJson
    /// <c>JsonStringEnumConverter(JsonNamingPolicy.CamelCase, true)</c>
    /// registered at
    /// <c>src/NzbDrone.Common/Serializer/System.Text.Json/STJson.cs:33</c>.
    /// The frontend TS enum at <c>frontend/src/typings/ExistingFileBehavior.ts</c>
    /// is locked to the same string encoding.
    ///
    /// 2-value cardinality per D-04 recommendation: manga has no episode-merge
    /// analog for Sonarr's "Combine" — a 3-value variant would be semantically
    /// meaningless on the manga side.
    /// </summary>
    public enum ExistingFileBehavior
    {
        Skip = 0,
        Replace = 1
    }
}
