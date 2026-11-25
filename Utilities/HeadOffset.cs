using BoneLib;
using HarmonyLib;
using Il2CppSLZ.Marrow;

namespace BoneSnap.Utilities;

/// <summary>
/// Handles temporary disabling of the rig's head offset.
/// </summary>
internal static class HeadOffset
{
    private static int _suppressors;

    public static IDisposable Suppress()
    {
        _suppressors++;
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _suppressors--;
        }
    }

    [HarmonyPatch(typeof(PlayerAvatarArt), nameof(PlayerAvatarArt.UpdateAvatarHead))]
    private static class UpdateAvatarHeadPatch
    {
        // ReSharper disable once InconsistentNaming
        // ReSharper disable once UnusedMember.Local
        private static bool Prefix(PlayerAvatarArt __instance)
        {
            if (_suppressors == 0)
                return true;

            var localRig = Player.RigManager;
            return localRig == null || __instance.GetComponentInParent<RigManager>() != localRig;
        }
    }
}
