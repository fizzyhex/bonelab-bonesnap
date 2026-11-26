using BoneLib;
using HarmonyLib;
using Il2CppSLZ.Marrow;
using UnityEngine;

namespace BoneSnap.Utilities;

/// <summary>
/// Handles temporary disabling of the rig's head offset.
/// </summary>
internal static class HeadOffset
{
    private static Transform? _head;
    private static Vector3 _unoffsetHeadPosition;

    public static IDisposable? Remove()
    {
        if (_head == null)
            return null;

        var offsetPosition = _head.localPosition;
        _head.localPosition = _unoffsetHeadPosition;
        return new RemovalScope(_head, offsetPosition);
    }

    private sealed class RemovalScope(Transform head, Vector3 offsetPosition) : IDisposable
    {
        public void Dispose()
        {
            if (head != null)
                head.localPosition = offsetPosition;
        }
    }

    [HarmonyPatch(typeof(PlayerAvatarArt), nameof(PlayerAvatarArt.UpdateAvatarHead))]
    private static class UpdateAvatarHeadPatch
    {
        // ReSharper disable once InconsistentNaming
        // ReSharper disable once UnusedMember.Local
        private static void Prefix(PlayerAvatarArt __instance)
        {
            var localRig = Player.RigManager;

            if (localRig == null || __instance.GetComponentInParent<RigManager>() != localRig)
                return;

            var animator = localRig.avatar != null ? localRig.avatar.animator : null;
            _head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;

            if (_head != null)
                _unoffsetHeadPosition = _head.localPosition;
        }
    }
}
