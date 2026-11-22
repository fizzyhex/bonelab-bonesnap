using BoneLib;
using Il2CppSLZ.Marrow;
using UnityEngine.Rendering;

namespace BoneSnap.Capture.CaptureHooks;

/// <summary>
/// If configured by the user, toggles head/hair meshes during capture.
/// </summary>
public class RevealLocalMeshes : ICaptureHook
{
    private PlayerAvatarArt? _avatarArt;
    private bool _headWasHidden;
    private bool _hairWasHidden;
    
    private void Reveal(RevealMeshesMode mode)
    {
        var rigManager = Player.RigManager;

        if (mode == RevealMeshesMode.None || rigManager == null || rigManager.avatar == null)
            return;

        _avatarArt = rigManager.GetComponentInChildren<PlayerAvatarArt>();

        if (!_avatarArt)
            return;

        var avatar = rigManager.avatar;

        _headWasHidden = mode.HasFlag(RevealMeshesMode.Head) &&
                            (avatar.headMeshes?.Any(mesh => mesh != null && !mesh.enabled) ?? false);
        _hairWasHidden = mode.HasFlag(RevealMeshesMode.Hair) &&
                            (avatar.hairMeshes?.Any(mesh => mesh != null && mesh.shadowCastingMode == ShadowCastingMode.ShadowsOnly) ?? false);

        if (_headWasHidden) _avatarArt.EnableHead();
        if (_hairWasHidden) _avatarArt.EnableHair();
    }

    public void PreCapture(CaptureParams captureParams)
    {
        Reveal(captureParams.RevealMeshes);
    }

    public void PostCapture(CaptureParams captureParams)
    {
        if (_avatarArt == null) return;
        if (_headWasHidden) _avatarArt.DisableHead();
        if (_hairWasHidden) _avatarArt.DisableHair();
    }
}