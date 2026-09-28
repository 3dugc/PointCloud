using System;

namespace Bujiaban.PointCloud.Immersal.Editor
{
    /// <summary>Validates any profile without depending on a host project's assets.</summary>
    public static class ImmersalProfileValidation
    {
        public static void Validate(ImmersalLocalizationProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            profile.CreateSnapshot();
        }
    }
}
