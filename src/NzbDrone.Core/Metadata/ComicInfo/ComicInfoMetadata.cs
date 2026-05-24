using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.MediaFiles.ChapterArchiving;
using LegacyWriter = NzbDrone.Core.MediaFiles.ChapterArchiving.Metadata.ComicInfo.ComicInfoMetadataWriter;

namespace NzbDrone.Core.Metadata.ComicInfo
{
    // Phase 30 Plan 30-04 D-02 Option 4b (R-1 mitigation per CONTEXT.md + PATTERNS.md
    // §Plan 30-04 Task 4) — DELEGATION wrap, NOT absorption. ComicInfoMetadata is the
    // new ThingiProvider; the existing ComicInfoMetadataWriter remains untouched in
    // src/NzbDrone.Core/MediaFiles/ChapterArchiving/Metadata/ComicInfo/ so Phase 4
    // golden-fixture XML tests (ComicInfoMetadataWriterFixture +
    // ComicInfoXmlBuilderFixture) keep passing verbatim against the writer's direct
    // construction surface.
    //
    // Delegation chain post Phase 30 D-02 flip:
    //   Cbz/FolderArchiver
    //     -> IMetadataFactory.Enabled()                  (Plan 30-04 Task 5 flip)
    //       -> ComicInfoMetadata.WriteAsync              (this class)
    //         -> ComicInfoMetadataWriter.WriteAsync      (untouched Phase 4 code)
    //
    // D-02 flip: AppliesTo no longer reads the legacy ConfigService formats key.
    // The Enable toggle on MetadataDefinition IS the canonical signal — flipping
    // enable/disable in Settings/Metadata is what controls ComicInfo.xml emission
    // post-Plan-30-04.
    public class ComicInfoMetadata : MetadataBase<ComicInfoMetadataSettings>
    {
        private readonly LegacyWriter _writer;

        public ComicInfoMetadata(LegacyWriter writer)
        {
            _writer = writer;
        }

        public override string Name => "ComicInfo";

        // Delegates to the existing writer's FormatKey ("comicinfo"); kept on the
        // IMetadata contract for Option 4b symmetry + future MetadataFormats-key
        // bookkeeping if a v1.3+ migration needs it. Archivers no longer USE this
        // value to filter writers post D-02 flip.
        public override string FormatKey => _writer.FormatKey;

        // D-02 flip — Definition.Enable IS the toggle now. Pre-flip the existing
        // legacy writer's AppliesTo checked the global ConfigService formats key;
        // that path is now bypassed because the archiver enumerates only enabled
        // providers via IMetadataFactory.Enabled() (Plan 30-04 Task 5), and this
        // method short-circuits if the substrate hasn't bound a Definition yet
        // (boot edge case prior to InitializeProviders inserting the default row).
        public override bool AppliesTo(ChapterArchiveRequest request)
        {
            return Definition?.Enable ?? false;
        }

        // Pure delegation per Option 4b — preserves Phase 4 golden-fixture XML
        // tests verbatim (the writer's WriteAsync emits the exact same bytes via
        // ComicInfoXmlBuilder regardless of who invokes it).
        public override Task WriteAsync(ChapterArchiveRequest request, ArchiveOutputContext ctx, CancellationToken ct)
        {
            return _writer.WriteAsync(request, ctx, ct);
        }
    }
}
