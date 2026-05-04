namespace NzbDrone.Core.Notifications.Kavita
{
    public interface IKavitaProxy
    {
        // Dispatches a rescan to Kavita. If Settings.LibraryId is set, scans that one library
        // (POST /api/Library/scan?libraryId=N); otherwise scans all libraries
        // (POST /api/Library/scan-all). Per Phase 6 D-16.
        void Scan(KavitaNotificationSettings settings);

        // Connectivity probe — forces a fresh JWT fetch to surface bad credentials. Throws on
        // failure; KavitaService.Test wraps the throw into a ValidationFailure.
        void Test(KavitaNotificationSettings settings);
    }
}
