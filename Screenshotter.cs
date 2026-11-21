using System.Diagnostics;
using BoneSnap.Assets;
using BoneSnap.SDK;
using BoneSnap.Utilities;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppOccaSoftware.Exposure.Runtime;
using BoneLib;
using Il2CppSLZ.Bonelab.SaveData;
using Il2CppSLZ.Marrow;
using Il2CppSLZ.Marrow.SaveData;
using MelonLoader;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using static BoneSnap.CoroutineUtils;

namespace BoneSnap;

internal class Screenshotter
{
    private const float Megabyte = 1024f;
    
    private const int SdkCaptureLimit = 5;
    private const float SdkCaptureWindowSeconds = 3;

    private readonly ReferenceCache _referenceCache;
    
    // Prevents evil SDK mods from spamming screenshots
    private readonly RateLimiter _sdkRateLimiter = new(SdkCaptureLimit, SdkCaptureWindowSeconds);
    
    private int _screenshotIndex;
    public string? LastCaptureFileName { get; private set; }

    public Screenshotter(ReferenceCache referenceCache)
    {
        _referenceCache = referenceCache;
    }

    private static string GenerateTimecode(DateTime time)
    {
        var datestamp = time.ToString("yyyy-MM-dd");
        var timestamp = time.ToString("HH.mm.ss");

        return datestamp + " " + timestamp;
    }

    private readonly record struct RawCapture(Il2CppStructArray<byte> Pixels, GraphicsFormat Format, int Width, int Height, int Quality);

    public async Task Capture(CaptureParams captureParams)
    {
        if (!TryRender(captureParams, out var capture))
            return;

        await OutputCapture(capture, captureParams.OutputPath);
    }

    /// <summary>
    /// Renders <see cref="CaptureParams.Camera"/> and reads its pixels back synchronously.
    /// </summary>
    /// <remarks>
    /// Touches the Unity API, so it must run on the main thread.
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

        var disabledAvatarArt = captureParams.ShiftHeadOffset ? DisableLocalAvatarArt() : null;
        var hiddenRenderers = HideRigRenderers(captureParams.HideRigs);

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var captureTexture = RenderCamera(captureParams.Camera, width, height);
            capture = ReadRawCapture(captureTexture, captureParams.Quality);
            UnityEngine.Object.Destroy(captureTexture);

            MelonLogger.Msg(ConsoleColor.Green, $"- Taken! (render {stopwatch.ElapsedMilliseconds} ms)");
        }
        finally
        {
            GraphicsManager.graphicsSettings.RenderScale = renderScale;

            foreach (var renderer in hiddenRenderers)
            {
                if (renderer != null)
                    renderer.enabled = true;
            }

            if (disabledAvatarArt != null)
                disabledAvatarArt.enabled = true;
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
        // The camera may already be rendering to a texture of its own
        var previousTargetTexture = camera.targetTexture;
        var previousActiveTexture = RenderTexture.active;

        var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.Default, RenderTextureReadWrite.sRGB);
        
        var captureTexture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);

        // Keep the HUD (e.g. the countdown) out of the capture
        var hud = SnapHUD.GetInstance();
        var hudWasActive = hud.IsActive();
        hud.Hide();

        try
        {
            camera.targetTexture = rt;
            camera.Render();

            RenderTexture.active = rt;
            captureTexture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);

            hud.FreezePreview(rt);
        }
        finally
        {
            camera.targetTexture = previousTargetTexture;
            RenderTexture.active = previousActiveTexture;
            RenderTexture.ReleaseTemporary(rt);

            if (hudWasActive)
                hud.Show();
        }

        return captureTexture;
    }

    private static RawCapture ReadRawCapture(Texture2D texture, int quality)
        => new(texture.GetRawTextureData(), texture.graphicsFormat, texture.width, texture.height, quality);

    // Quality 100=png
    private static string GetFormat(int quality) => quality == 100 ? "png" : "jpg";

    /// <summary>
    /// Encodes <paramref name="capture"/> on a worker thread, as encoding large captures can take seconds.
    /// </summary>
    private static Task<byte[]> EncodeAsync(RawCapture capture)
    {
        // A dedicated thread, so it can be safely attached to (and detached from) the IL2CPP domain
        return Task.Factory.StartNew(() =>
        {
            var thread = IL2CPP.il2cpp_thread_attach(IL2CPP.il2cpp_domain_get());

            try
            {
                return Encode(capture);
            }
            finally
            {
                IL2CPP.il2cpp_thread_detach(thread);
            }
        }, TaskCreationOptions.LongRunning);
    }

    private static byte[] Encode(RawCapture capture)
    {
        var pixels = new Il2CppSystem.Array(capture.Pixels.Pointer);
        var width = (uint)capture.Width;
        var height = (uint)capture.Height;

        // JPGs are bumped up a step to make full JPG quality reachable
        return GetFormat(capture.Quality) == "png"
            ? ImageConversion.EncodeArrayToPNG(pixels, capture.Format, width, height)
            : ImageConversion.EncodeArrayToJPG(pixels, capture.Format, width, height,
                quality: Mathf.Min(capture.Quality + CapturePrefs.CaptureQualityIncrement, 100));
    }

    /// <summary>
    /// Encodes and writes <paramref name="capture"/> in the background. Call from the main thread!
    /// </summary>
    private Task OutputCapture(RawCapture capture, string outputPath)
    {
        Directory.CreateDirectory(outputPath);

        var captureName = NewCaptureName(GetFormat(capture.Quality));
        var output = EncodeAndWriteAsync(capture, outputPath + $"/{captureName}");

        SnapHUD.GetInstance().SwapState(new EncodingInProgressState());
        MelonCoroutines.Start(WaitForTaskAndThenRunCallback(output, OnOutputFinished));

        return output;

        void OnOutputFinished(Task task)
        {
            SnapHUD.GetInstance().SwapState(task.IsCompletedSuccessfully
                ? new SaveCompleteState(captureName, ((Task<long>)task).Result)
                : new IdleState());
        }
    }

    /// <returns>The time spent encoding, in milliseconds.</returns>
    private static async Task<long> EncodeAndWriteAsync(RawCapture capture, string filePath)
    {
        var stopwatch = Stopwatch.StartNew();

        var bytes = await EncodeAsync(capture);
        var encodeMs = stopwatch.ElapsedMilliseconds;

        LogFileSize(bytes, filePath);
        await WriteToFileAsync(filePath, bytes);
        LogOutputCompletion(encodeMs, stopwatch.ElapsedMilliseconds - encodeMs);

        return encodeMs;
    }

    private async Task OutputFisheyeCapture(CaptureParams captureParams)
    {
        var spectatorReferences = _referenceCache.GetSpectatorReferences();
        var spectatorCamera = spectatorReferences.Camera;
        var rigScreenOptions = spectatorReferences.RigScreenOptions;

        if (spectatorCamera == null)
        {
            MelonLogger.Warning("Could not find the spectator camera!");
            return;
        }

        if (rigScreenOptions == null)
        {
            MelonLogger.Warning("Rig screen options missing!");
            return;
        }

        // Apply temporary settings overrides

        var autoExposureMode = spectatorReferences.AutoExposure?.mode.value;
        var autoExposureActive = spectatorReferences.AutoExposure == null || spectatorReferences.AutoExposure.active;
        var spectatorSettings = rigScreenOptions.GetSettings();

        if (spectatorReferences.AutoExposure != null)
        {
            spectatorReferences.AutoExposure.active = false;
            spectatorReferences.AutoExposure.mode = new AutoExposureModeParameter(AutoExposureMode.Off);
        }

        var disabledOverrides = captureParams.DisabledVolumeComponents is { Count: > 0 } disabledComponents
            ? DisableVolumeOverrides(disabledComponents)
            : [];

        var spectatorCameraMode = spectatorSettings.SpectatorCameraMode;
        var enablingFisheye = spectatorCameraMode != SpectatorCameraMode.Fisheye;

        if (enablingFisheye)
        {
            MelonLogger.Msg("Fisheye is off - it will be temporarily enabled for the capture.");
            rigScreenOptions.SetSpectatorCamera(SpectatorCameraMode.Fisheye, rigScreenOptions.eyeTarget);

            // the camera will be floating off in the scene somewhere, so move it to the follower'for the capture
            if (spectatorReferences.Follower?.targetTransform is { } followTarget)
                spectatorCamera.transform.SetPositionAndRotation(followTarget.position, followTarget.rotation);
        }

        RawCapture capture;

        try
        {
            if (!TryRender(captureParams with { Camera = spectatorCamera }, out capture))
                return;
        }
        finally
        {
            // Clean-up & remove any overrides

            rigScreenOptions.SetSpectatorCamera(spectatorCameraMode, rigScreenOptions.eyeTarget);

            foreach (var volumeComponent in disabledOverrides)
                volumeComponent.active = true;

            if (autoExposureMode != null && spectatorReferences.AutoExposure != null)
            {
                spectatorReferences.AutoExposure.active = autoExposureActive;
                spectatorReferences.AutoExposure.mode = new AutoExposureModeParameter((AutoExposureMode)autoExposureMode);
            }
        }

        await OutputCapture(capture, captureParams.OutputPath);
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

    private static PlayerAvatarArt? DisableLocalAvatarArt()
    {
        var rigManager = Player.RigManager;

        if (rigManager == null)
            return null;

        var avatarArt = rigManager.GetComponentInChildren<PlayerAvatarArt>();

        if (avatarArt == null || !avatarArt.enabled)
            return null;

        avatarArt.enabled = false;
        return avatarArt;
    }

    private static List<Renderer> HideRigRenderers(HideRigsMode mode)
    {
        var hiddenRenderers = new List<Renderer>();

        RigManager[] rigManagers = mode switch
        {
            HideRigsMode.Local when Player.RigManager != null => [Player.RigManager],
            // Includes the rigs of other players when Fusion is installed
            HideRigsMode.All => UnityEngine.Object.FindObjectsOfType<RigManager>().ToArray(),
            _ => []
        };

        foreach (var rigManager in rigManagers)
        {
            foreach (var renderer in rigManager.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled)
                    continue;

                renderer.enabled = false;
                hiddenRenderers.Add(renderer);
            }
        }

        return hiddenRenderers;
    }

    private string NewCaptureName(string? format)
    {
        var timecode = GenerateTimecode(DateTime.Now);
        _screenshotIndex++;

        var result = $"{timecode} {_screenshotIndex}.{format}";
        LastCaptureFileName = result;
        return result;
    }

    private static void LogFileSize(byte[] bytes, string filePath)
    {
        var fileSizeInMegabytes = bytes.Length / Megabyte / Megabyte;
        MelonLogger.Msg($"{fileSizeInMegabytes:F2} MB capture -> {filePath}...");
    }

    private static void LogOutputCompletion(float encodeMs, float writeMs)
    {
        MelonLogger.Msg(ConsoleColor.Green, $"- Saved! (encode {encodeMs} ms, write {writeMs} ms)");
        MelonLogger.Msg("");
    }

    private async Task OutputScreenshot(CaptureParams captureParams)
    {
        MelonLogger.Msg("Capturing...");

        // Capture the entire screen (the screen can't be read at any other size, so the requested size is ignored)

        var width = Screen.width;
        var height = Screen.height;
        var screenshotTexture = new Texture2D(width, height, TextureFormat.RGB24, false);

        // Read screen contents into the texture

        screenshotTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);

        // Upload the pixels so the preview can sample them
        screenshotTexture.Apply(false);
        SnapHUD.GetInstance().FreezePreview(screenshotTexture);

        var capture = ReadRawCapture(screenshotTexture, captureParams.Quality);
        UnityEngine.Object.Destroy(screenshotTexture);

        // System.Windows.Forms isn't bundled with MelonLoader anymore :(

        //Clipboard.SetImage(
        //    (Bitmap)((new ImageConverter()).ConvertFrom(bytes))
        //);

        await OutputCapture(capture, captureParams.OutputPath);
    }

    private static async Task WriteToFileAsync(string path, byte[] imageData)
    {
        await using var stream = new FileStream(path, FileMode.Create);
        await stream.WriteAsync(imageData);
    }

    private void PrepareCapture(float waitTime, Action callback)
    {
        if (AssetManager.CaptureTimerAudio != null)
            AudioManager.Play(AssetManager.CaptureTimerAudio);

        MelonCoroutines.Start(WaitAndThenRunCallback(waitTime, callback));
    }

    public void PrepareFisheyeCapture(float waitTime, CaptureParams captureParams)
    {
        var spectatorReferences = _referenceCache.GetSpectatorReferences();

        // The spectator camera only follows the head in fisheye mode
        var followingHead = spectatorReferences.RigScreenOptions?.GetSettings().SpectatorCameraMode == SpectatorCameraMode.Fisheye;
        var previewAnchor = followingHead ? null : spectatorReferences.Follower?.targetTransform;

        SnapHUD.GetInstance().SwapState(new CountdownState(waitTime, spectatorReferences.Camera, previewAnchor));
        PrepareCapture(waitTime, () => RunCapture(() => OutputFisheyeCapture(captureParams)));
    }

    public void PrepareScreenshotCapture(float waitTime, CaptureParams captureParams)
    {
        PrepareCapture(waitTime, () => RunCapture(() => OutputScreenshot(captureParams)));
    }

    private static void RunCapture(Func<Task> capture)
    {
        if (AssetManager.CaptureStartAudio != null) AudioManager.Play(AssetManager.CaptureStartAudio);

        // Awaiting resumes off the main thread, play completion audio from a coroutine
        MelonCoroutines.Start(WaitForTaskAndThenRunCallback(capture(), OnCaptureFinished));
        return;

        static void OnCaptureFinished(Task task)
        {
            if (task.IsFaulted)
            {
                MelonLogger.Error($"Capture failed: {task.Exception}");
                return;
            }

            if (AssetManager.CaptureCompleteAudio != null) AudioManager.Play(AssetManager.CaptureCompleteAudio);
        }
    }
    
    public bool TrySdkCapture(Camera camera, BoneSnapPreferences preferences)
    {
        if (!_sdkRateLimiter.TryAcquire())
        {
            MelonLogger.Warning(
                $"SDK capture hit the rate limit! ({SdkCaptureLimit}/{SdkCaptureWindowSeconds}s)");
            return false;
        }

        RunSdkCapture();
        return true;

        async void RunSdkCapture()
        {
            try
            {
                await Capture(CaptureParams.FromPreferences(preferences, camera));
            }
            catch (Exception e)
            {
                MelonLogger.Error($"SDK capture failed: {e}");
            }
        }
    }
}
