using MelonLoader;

namespace BoneSnap.Utilities;

public class PlatformHelper
{
    // From Fusion
    // https://github.com/Lakatrazz/BONELAB-Fusion/blob/59f57e26a9877dfb295b8b6d773be1c9f5243a33/LabFusion/src/Utilities/PlatformHelper.cs#L7
    private static readonly bool IsAndroidCached = MelonUtils.CurrentPlatform == (MelonPlatformAttribute.CompatiblePlatforms)3;
    public static bool IsAndroid => IsAndroidCached;
}