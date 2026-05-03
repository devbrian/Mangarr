using System.Collections.Generic;

namespace NzbDrone.Core.Notifications.Komga
{
    public interface IKomgaProxy
    {
        void Scan(KomgaNotificationSettings settings);
        List<KomgaLibrary> GetLibraries(KomgaNotificationSettings settings);
    }
}
