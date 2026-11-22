using System.Collections;
using System.Diagnostics;
using BoneSnap.SDK;
using Il2CppSLZ.Bonelab.SaveData;
using Il2CppSLZ.Marrow.SaveData;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoneSnap.Capture;

/// <summary>
/// Captures the camera (for Fisheye and the SDK script).
/// </summary>
internal partial class Screenshotter
{
    public void PrepareFisheyeCapture(float waitTime, CaptureParams captureParams)
    {
        var spectatorReferences = _referenceCache.GetSpectatorReferences();

        // The spectator camera only follows the head in fisheye mode
        var followingHead = spectatorReferences.RigScreenOptions?.GetSettings().SpectatorCameraMode == SpectatorCameraMode.Fisheye;
        var previewAnchor = followingHead ? null : spectatorReferences.Follower?.targetTransform;

        SnapHUD.GetInstance().SwapState(new CountdownState(waitTime, spectatorReferences.Camera, previewAnchor));
        StartCountdown(waitTime, captureParams, CaptureFisheye);
    }

    // ReSharper disable once UnusedMethodReturnValue.Global
    public bool TrySdkCapture(Camera camera, BoneSnapPreferences preferences)
    {
        if (!_sdkRateLimiter.TryAcquire())
        {
            MelonLogger.Warning(
                $"SDK capture hit the rate limit! ({SdkCaptureLimit}/{SdkCaptureWindowSeconds}s)");
            return false;
        }

        var captureParams = CaptureParams.FromPreferences(preferences, camera) with
        {
            ShiftHeadOffset = preferences.ShiftHeadOffset.Value,
            RevealMeshes = preferences.RevealMeshes.Value
        };

        MelonCoroutines.Start(RunCapture(captureParams, CaptureCamera, OnSdkCaptureFinished));
        return true;

        static void OnSdkCaptureFinished(Task task)
        {
            if (task.IsFaulted)
                MelonLogger.Error($"SDK capture failed: {task.Exception}");
        }
    }

    /// <summary>
    /// Renders the spectator camera in fisheye mode with the configured volume components disabled.
    /// </summary>
    private IEnumerator CaptureFisheye(CaptureParams captureParams, CaptureResult result)
    {
        var spectatorReferences = _referenceCache.GetSpectatorReferences();
        var spectatorCamera = spectatorReferences.Camera;
        var rigScreenOptions = spectatorReferences.RigScreenOptions;

        if (spectatorCamera == null)
        {
            MelonLogger.Warning("Could not find the spectator camera!");
            yield break;
        }

        if (rigScreenOptions == null)
        {
            MelonLogger.Warning("Rig screen options missing!");
            yield break;
        }

        var spectatorSettings = rigScreenOptions.GetSettings();

        var disabledOverrides = captureParams.DisabledVolumeComponents is { Count: > 0 } disabledComponents
            ? DisableVolumeOverrides(disabledComponents)
            : [];

        var spectatorCameraMode = spectatorSettings.SpectatorCameraMode;
        var enablingFisheye = spectatorCameraMode != SpectatorCameraMode.Fisheye;

        if (enablingFisheye)
        {
            MelonLogger.Msg("Fisheye is off - it will be temporarily enabled for the capture.");
            rigScreenOptions.SetSpectatorCamera(SpectatorCameraMode.Fisheye, rigScreenOptions.eyeTarget);

            // After switching to fisheye, the camera may still be away from the follower target. Align it before
            // rendering.
            if (spectatorReferences.Follower?.targetTransform is { } followTarget)
                spectatorCamera.transform.SetPositionAndRotation(followTarget.position, followTarget.rotation);
        }

        try
        {
            if (TryRender(captureParams with { Camera = spectatorCamera }, out var capture))
                result.Capture = capture;
        }
        finally
        {
            rigScreenOptions.SetSpectatorCamera(spectatorCameraMode, rigScreenOptions.eyeTarget);

            foreach (var volumeComponent in disabledOverrides)
                volumeComponent.active = true;
        }
    }

    private static IEnumerator CaptureCamera(CaptureParams captureParams, CaptureResult result)
    {
        if (TryRender(captureParams, out var capture))
            result.Capture = capture;
        
        yield break;
    }

    /// <summary>
    /// Renders <see cref="CaptureParams.Camera"/> and reads its pixels back synchronously.
    /// </summary>
    /// <remarks>
    /// Unity API calls need to run on the main thread to avoid crashes.
    /// </remarks>
    private static bool TryRender(CaptureParams captureParams, out RawCapture capture)
    {
        capture = default;

        if (captureParams.Camera == null)
        {
            MelonLogger.Warning("No camera was provided for the capture!");
            return false;
        }

        MelonLogger.Msg("Capturing...");

        // Render scales other than 1 can cause the ambient occlusion to become offset
        var renderScale = GraphicsManager.graphicsSettings.RenderScale;
        GraphicsManager.graphicsSettings.RenderScale = 1;

        var (width, height) = ClampToMaxTextureSize(captureParams.Width, captureParams.Height);

        var captureHooks = new CaptureHooks.CaptureHooks();

        try
        {
            var stopwatch = Stopwatch.StartNew();
            captureHooks.PreCapture(captureParams);

            var captureTexture = RenderCamera(captureParams.Camera, width, height);
            capture = ReadRawCapture(captureTexture, captureParams.Quality);
            UnityEngine.Object.Destroy(captureTexture);

            MelonLogger.Msg(ConsoleColor.Green, $"- Taken! (render {stopwatch.ElapsedMilliseconds} ms)");
        }
        finally
        {
            GraphicsManager.graphicsSettings.RenderScale = renderScale;
            captureHooks.PostCapture(captureParams);
        }

        return true;
    }

    public static (int Width, int Height) ClampToMaxTextureSize(int width, int height)
    {
        var maxTextureSize = SystemInfo.maxTextureSize;
        var largestSide = Math.Max(width, height);

        if (largestSide <= maxTextureSize)
            return (width, height);

        var scale = (double)maxTextureSize / largestSide;
        var clampedWidth = Math.Min(maxTextureSize, Math.Max(1, (int)Math.Floor(width * scale)));
        var clampedHeight = Math.Min(maxTextureSize, Math.Max(1, (int)Math.Floor(height * scale)));

        return (clampedWidth, clampedHeight);
    }

    private static Texture2D RenderCamera(Camera camera, int width, int height)
    {
        var previousTargetTexture = camera.targetTexture;

        // Auto exposure multiplies gamma-encoded colours when the camera targets an sRGB texture, which blows out
        // the image compared to the game window. So we have to render as linear, then encode to sRGB with a blit

        var linearTexture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.sRGB);

        try
        {
            camera.targetTexture = linearTexture;
            camera.Render();
            Graphics.Blit(linearTexture, rt);

            var captureTexture = ReadPixels(rt);
            SnapHUD.GetInstance().FreezePreview(rt);

            return captureTexture;
        }
        finally
        {
            camera.targetTexture = previousTargetTexture;
            RenderTexture.ReleaseTemporary(linearTexture);
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    private static List<VolumeComponent> DisableVolumeOverrides(IReadOnlySet<string> componentNames)
    {
        var disabledOverrides = new List<VolumeComponent>();

        foreach (var volume in UnityEngine.Object.FindObjectsOfType<Volume>())
        {
            if (!volume.isActiveAndEnabled)
                continue;

            var profile = volume.HasInstantiatedProfile() ? volume.profile : volume.sharedProfile;

            if (profile == null)
                continue;

            foreach (var volumeComponent in profile.components)
            {
                if (!volumeComponent.active || !componentNames.Contains(volumeComponent.GetIl2CppType().Name))
                    continue;

                volumeComponent.active = false;
                disabledOverrides.Add(volumeComponent);
            }
        }

        return disabledOverrides;
    }
}
