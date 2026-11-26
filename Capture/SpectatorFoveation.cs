using BoneLib;
using Il2CppSLZ.Graphics2.VRS;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoneSnap.Capture;

/// <summary>
/// 'Disables' foveated rendering for the spectator camera.
/// </summary>
internal static class SpectatorFoveation
{
    // Any value well beyond the screen corners works
    private const float UnfoveatedRadius = 10f;

    private static ReferenceCache _referenceCache = null!;

    private static bool _levelReady;
    private static bool _disabled;

    public static void Initialize(ReferenceCache referenceCache)
    {
        _referenceCache = referenceCache;

        Hooking.OnLevelLoaded += _ => _levelReady = true;
        Hooking.OnLevelUnloaded += () => _levelReady = _disabled = false;

        RenderPipelineManager.add_beginCameraRendering(
            (Il2CppSystem.Action<ScriptableRenderContext, Camera>)OnBeginCameraRendering);
    }

    private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (_disabled || !_levelReady || camera != _referenceCache.GetSpectatorReferences().Camera)
            return;

        _disabled = true;

        var vrsOverride = camera.GetComponent<CameraVRSOverride>();

        if (vrsOverride == null)
            return;

        var radii = vrsOverride.radii;
        radii.RDMInnerRadius = UnfoveatedRadius;
        radii.RDMOuterRadius = UnfoveatedRadius;
        radii.VRSInnerElipseMinorRadius = UnfoveatedRadius;
        radii.VRSInnerElipseMajorRadius = UnfoveatedRadius;
        radii.VRSOuterElipseMinorRadius = UnfoveatedRadius;
        radii.VRSOuterElipseMajorRadius = UnfoveatedRadius;
        vrsOverride.radii = radii;
    }
}
