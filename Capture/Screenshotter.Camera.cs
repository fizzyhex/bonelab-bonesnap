using System.Collections;
using System.Diagnostics;
using BoneSnap.SDK;
using BoneSnap.Utilities;
using Il2CppSLZ.Bonelab.SaveData;
using Il2CppSLZ.Marrow.SaveData;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BoneSnap.Capture;

/// <summary>
/// Captures the camera (for Fisheye and the SDK script).
/// </summary>
internal partial class Screenshotter
{
    /// Layer used for composited captures.
    /// 31 is the background layer in Bonelab. It's used by a couple sprite's on UI elements, but it should (hopefully)
    /// be mostly clear otherwise :P
    public const int CompositingLayer = 31;
    
    public void PrepareFisheyeCapture(float waitTime, CaptureParams captureParams)
    {
        var (camera, anchor) = GetFisheyePreviewSource();

        SnapHUD.GetInstance().SwapState(new CountdownState(waitTime, camera, anchor, previewParams: () => captureParams));
        StartCountdown(waitTime, captureParams, CaptureFisheye);
    }

    /// <summary>
    /// Gets the camera that fisheye captures render
    /// </summary>
    private (Camera? Camera, Transform? Anchor) GetFisheyePreviewSource()
    {
        var spectatorReferences = _referenceCache.GetSpectatorReferences();

        // The spectator camera only follows the head in fisheye mode
        var followingHead = spectatorReferences.RigScreenOptions?.GetSettings().SpectatorCameraMode == SpectatorCameraMode.Fisheye;
        var anchor = followingHead ? null : spectatorReferences.Follower?.targetTransform;

        return (spectatorReferences.Camera, anchor);
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
        if (captureParams.ShiftHeadOffset)
            yield return new WaitForEndOfFrame();

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

        // Disable lens distortion even when Camera Overrides is off to keep the colour pass aligned with
        // the matte.
        if (captureParams.CompositingMode != CompositingMode.Combined)
        {
            captureParams = captureParams with
            {
                DisabledVolumeComponents = (captureParams.DisabledVolumeComponents ?? new HashSet<string>())
                    .Union(LensDistortionComponents)
                    .ToHashSet()
            };
        }

        try
        {
            using (CameraAdjustments.Apply(spectatorCamera, captureParams))
            using (AutoExposureFreeze.Apply(spectatorCamera))
            using (captureParams.ShiftHeadOffset ? HeadOffset.Remove() : null)
            {
                CompositingLayer[] layers = captureParams.CompositingMode switch
                {
                    CompositingMode.Multiple =>
                        [Capture.CompositingLayer.PlayerMask, Capture.CompositingLayer.Background, Capture.CompositingLayer.Combined],
                    CompositingMode.CookieCutout =>
                        [Capture.CompositingLayer.PlayerCutout, Capture.CompositingLayer.Background, Capture.CompositingLayer.Combined],
                    _ => [Capture.CompositingLayer.Combined]
                };

                var captures = new List<RawCapture>();

                foreach (var layer in layers)
                {
                    if (TryRender(captureParams with { Camera = spectatorCamera, CompositingLayer = layer }, out var capture))
                        captures.Add(capture);
                }

                if (captures.Count > 0)
                    result.Captures = captures.ToArray();
            }
        }
        finally
        {
            rigScreenOptions.SetSpectatorCamera(spectatorCameraMode, rigScreenOptions.eyeTarget);
        }
    }

    private static IEnumerator CaptureCamera(CaptureParams captureParams, CaptureResult result)
    {
        if (captureParams.ShiftHeadOffset)
            yield return new WaitForEndOfFrame();

        using (captureParams.ShiftHeadOffset ? HeadOffset.Remove() : null)
        {
            if (TryRender(captureParams, out var capture))
                result.Captures = [capture];
        }
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

            Texture2D captureTexture;

            // Some post-processing effects cause issues with transparency & misalignment (HBAO)
            using (IsPlayerLayer(captureParams.CompositingLayer)
                       ? CameraAdjustments.Apply(captureParams.Camera, captureParams with
                       {
                           DisabledVolumeComponents = PostProcessingEffect.AllVolumeComponents,
                           FieldOfView = null
                       })
                       : null)
            {
                captureTexture = RenderCamera(captureParams.Camera,
                    width,
                    height,
                    captureParams.CompositingLayer);
            }

            Texture2D? colorTexture = null;

            if (captureParams.CompositingLayer == Capture.CompositingLayer.PlayerMask)
            {
                colorTexture = RenderCamera(captureParams.Camera,
                    width,
                    height,
                    captureParams.CompositingLayer,
                    keepPostProcessing: true);
            }

            capture = ReadRawCapture(captureTexture, captureParams.Quality, captureParams.CompositingLayer) with
            {
                ColorPixels = colorTexture?.GetRawTextureData()
            };

            UnityEngine.Object.Destroy(captureTexture);

            if (colorTexture != null)
                UnityEngine.Object.Destroy(colorTexture);

            MelonLogger.Msg(ConsoleColor.Green, $"- Taken! (render {stopwatch.ElapsedMilliseconds} ms)");
        }
        finally
        {
            GraphicsManager.graphicsSettings.RenderScale = renderScale;
            captureHooks.PostCapture(captureParams);
        }

        return true;
    }

    private static readonly IReadOnlySet<string> LensDistortionComponents = PostProcessingEffect.All
        .Where(effect => effect.Key == "LensDistortion")
        .SelectMany(effect => effect.VolumeComponents)
        .ToHashSet();

    private static bool IsPlayerLayer(CompositingLayer layer)
        => layer is Capture.CompositingLayer.PlayerMask or Capture.CompositingLayer.PlayerCutout;

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

    private static Texture2D RenderCamera(Camera camera, int width, int height, CompositingLayer compositing,
        bool keepPostProcessing = false)
    {
        var previousTargetTexture = camera.targetTexture;

        // Auto-exposure multiplies gamma-encoded colours when the camera targets an sRGB texture, which blows out
        // the image compared to the game window. So we have to render as linear, then encode to sRGB with a blit

        var linearTexture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.sRGB);

        var oldCullingMask = camera.cullingMask;
        var oldClearFlags = camera.clearFlags;

        var cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
        var oldRenderPostProcessing = cameraData && cameraData.renderPostProcessing;
        var oldBackgroundColor = camera.backgroundColor;

        try
        {
            if (compositing == Capture.CompositingLayer.Background)
            {
                // Exclude players
                camera.cullingMask &= ~(1 << CompositingLayer);
            }
            else if (IsPlayerLayer(compositing))
            {
                // Players only
                camera.cullingMask = 1 << CompositingLayer;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;

                // URP 12's post-processing doesn't properly support transparency.
                // Can mess with changing this when Bonelab gets a Unity upgrade eventually, hahaaaa.... ;-;
                if (cameraData && !keepPostProcessing)
                {
                    cameraData.renderPostProcessing = false;
                }
            }
            
            camera.targetTexture = linearTexture;
            camera.Render();
            Graphics.Blit(linearTexture, rt);

            var captureTexture = ReadPixels(rt, transparent: IsPlayerLayer(compositing) && !keepPostProcessing);
            SnapHUD.GetInstance().FreezePreview(rt);

            return captureTexture;
        }
        finally
        {
            camera.targetTexture = previousTargetTexture;
            camera.cullingMask = oldCullingMask;
            camera.clearFlags = oldClearFlags;
            camera.backgroundColor = oldBackgroundColor;

            if (cameraData)
                cameraData.renderPostProcessing = oldRenderPostProcessing;

            RenderTexture.ReleaseTemporary(linearTexture);
            RenderTexture.ReleaseTemporary(rt);
        }
    }
}
