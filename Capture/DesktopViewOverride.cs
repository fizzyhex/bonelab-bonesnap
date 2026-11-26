using BoneLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoneSnap.Capture;

/// <summary>
/// Applies the camera overrides to the spectator camera.
/// </summary>
internal static class DesktopViewOverride
{
    private static BoneSnapPreferences _preferences;
    private static ReferenceCache _referenceCache = null!;
    private static CameraAdjustments? _adjustments;

    private static bool _levelReady;

    public static void Initialize(BoneSnapPreferences preferences, ReferenceCache referenceCache)
    {
        _preferences = preferences;
        _referenceCache = referenceCache;

        Hooking.OnLevelLoaded += _ => _levelReady = true;
        Hooking.OnLevelUnloaded += () => _levelReady = false;

        RenderPipelineManager.add_beginCameraRendering(
            (Il2CppSystem.Action<ScriptableRenderContext, Camera>)OnBeginCameraRendering);
        RenderPipelineManager.add_endCameraRendering(
            (Il2CppSystem.Action<ScriptableRenderContext, Camera>)OnEndCameraRendering);
    }

    private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        Restore();

        if (!_levelReady || !_preferences.OverrideDesktopView.Value || !_preferences.CameraOverrides.Value)
            return;

        if (camera.targetTexture != null || camera != _referenceCache.GetSpectatorReferences().Camera)
            return;

        var captureParams = CaptureParams.FromPreferences(_preferences);
        _adjustments = CameraAdjustments.Apply(camera, captureParams);

        // SLZ URP updates the volume stack *before* this callback, so it has to be rebuilt with the overrides
        if (captureParams.DisabledVolumeComponents is { Count: > 0 })
            UpdateVolumeStack(camera);
    }

    private static void UpdateVolumeStack(Camera camera)
    {
        var cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
        var trigger = cameraData && cameraData.volumeTrigger ? cameraData.volumeTrigger : camera.transform;

        VolumeManager.instance.Update(trigger, CameraAdjustments.GetVolumeLayerMask(camera));
    }

    private static void OnEndCameraRendering(ScriptableRenderContext context, Camera camera) => Restore();

    private static void Restore()
    {
        _adjustments?.Dispose();
        _adjustments = null;
    }
}
