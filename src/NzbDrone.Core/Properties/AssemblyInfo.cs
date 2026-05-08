using System.Runtime.CompilerServices;

// Sonarr divergence: Phase 15 Plan 15-11 cascade absorption -- InternalsVisibleTo target updated to
// Mangarr.Core.Test (matches the assembly rename in Plan 15-04). Used by VideoFileInfoReaderFixture
// to access internal FFMpegCoreSideDataTypes constants in HDR-detection test cases.
[assembly: InternalsVisibleTo("Mangarr.Core.Test")]
