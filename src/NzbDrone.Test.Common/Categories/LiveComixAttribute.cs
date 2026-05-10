using NUnit.Framework;

namespace NzbDrone.Test.Common.Categories
{
    public class LiveComixAttribute : CategoryAttribute
    {
        public LiveComixAttribute()
            : base("LiveComix")
        {
        }
    }
}
