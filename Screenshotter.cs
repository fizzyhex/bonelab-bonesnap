using System.Diagnostics;
using BoneSnap.Assets;
using BoneSnap.Utilities;
using Il2CppOccaSoftware.Exposure.Runtime;
using Il2CppSLZ.Bonelab.SaveData;
using Il2CppSLZ.Marrow.SaveData;
using MelonLoader;
using UnityEngine;
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

    /// <summary>
    /// Renders <see cref="CaptureParams.Camera"/> at the requested size and writes the result to the output path.
    /// </summary>
    public async Task Capture(CaptureParams captureParams)
    {
        if (captureParams.Camera == null)
        {
            MelonLogger.Warning("No camera was provided for the capture!");
            return;
        }

        MelonLogger.Msg("Capturing...");

        // Render scales other than 1 can cause the ambient occlusion to become offset
        var renderScale = GraphicsManager.graphicsSettings.RenderScale;
        GraphicsManager.graphicsSettings.RenderScale = 1;

        byte[] bytes;
        string format;

        var (width, height) = ClampToMaxTextureSize(captureParams.Width, captureParams.Height);

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var captureTexture = RenderCamera(captureParams.Camera, width, height);
            var renderMs = stopwatch.ElapsedMilliseconds;

            stopwatch.Restart();
            bytes = Encode(captureTexture, captureParams.Quality, out format);
            var encodeMs = stopwatch.ElapsedMilliseconds;
            UnityEngine.Object.Destroy(captureTexture);

            MelonLogger.Msg(ConsoleColor.Green, $"- Taken! (render {renderMs} ms, encode {encodeMs} ms)");
        }
        finally
        {
            GraphicsManager.graphicsSettings.RenderScale = renderScale;
        }

        await OutputCapture(bytes, format, captureParams.OutputPath);
    }

    /// <summary>
    /// Shrinks size within the bounds of <see cref="SystemInfo.maxTextureSize"/> while maintaing aspect ratio.
    /// </summary>
    private static (int Width, int Height) ClampToMaxTextureSize(int width, int height)
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

        var rt = new RenderTexture(width, height, 24);
        var captureTexture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);

        try
        {
            camera.targetTexture = rt;
            camera.Render();

            RenderTexture.active = rt;
            captureTexture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            captureTexture.Apply();
        }
        finally
        {
            camera.targetTexture = previousTargetTexture;
            RenderTexture.active = previousActiveTexture;
            UnityEngine.Object.Destroy(rt);
        }

        return captureTexture;
    }

    private static byte[] Encode(Texture2D texture, int quality, out string format)
    {
        format = quality == 100 ? "png" : "jpg";

        // A quality of 100 means PNG, so JPGs are bumped up a step to make full JPG quality reachable
        return format == "png"
            ? texture.EncodeToPNG()
            : texture.EncodeToJPG(quality: Mathf.Min(quality + CapturePrefs.CaptureQualityIncrement, 100));
    }

    private async Task OutputCapture(byte[] bytes, string format, string outputPath)
    {
        var stopwatch = Stopwatch.StartNew();

        Directory.CreateDirectory(outputPath);
        var filePath = outputPath + $"/{NewCaptureName(format)}";

        LogFileSize(bytes, filePath);
        await WriteToFileAsync(filePath, bytes);
        LogOutputCompletion(stopwatch.ElapsedMilliseconds);
    }

    private async Task OutputFisheyeCapture(CaptureParams captureParams)
    {
        var spectatorReferences = _referenceCache.GetSpectatorReferences();

        if (spectatorReferences.Camera == null)
        {
            MelonLogger.Warning("Could not find the spectator camera!");
            return;
        }

        if (spectatorReferences.RigScreenOptions == null)
        {
            MelonLogger.Warning("Rig screen options missing!");
            return;
        }

        // Apply temporary settings overrides

        var autoExposureMode = spectatorReferences.AutoExposure?.mode.value;
        var autoExposureActive = spectatorReferences.AutoExposure == null || spectatorReferences.AutoExposure.active;
        var spectatorSettings = spectatorReferences.RigScreenOptions.GetSettings();

        if (spectatorReferences.AutoExposure != null)
        {
            spectatorReferences.AutoExposure.active = false;
            spectatorReferences.AutoExposure.mode = new AutoExposureModeParameter(AutoExposureMode.Off);
        }

        var spectatorCameraMode = spectatorSettings.SpectatorCameraMode;

        if (spectatorSettings.SpectatorCameraMode != SpectatorCameraMode.Fisheye)
        {
            MelonLogger.Msg("Fisheye is off - it will be temporarily enabled for the capture.");
            spectatorReferences.RigScreenOptions.SetSpectatorCamera(SpectatorCameraMode.Fisheye, spectatorReferences.RigScreenOptions.eyeTarget);
        }

        try
        {
            await Capture(captureParams with { Camera = spectatorReferences.Camera });
        }
        finally
        {
            // Clean-up & remove any overrides

            spectatorReferences.RigScreenOptions.SetSpectatorCamera(spectatorCameraMode, spectatorReferences.RigScreenOptions.eyeTarget);

            if (autoExposureMode != null && spectatorReferences.AutoExposure != null)
            {
                spectatorReferences.AutoExposure.active = autoExposureActive;
                spectatorReferences.AutoExposure.mode = new AutoExposureModeParameter((AutoExposureMode)autoExposureMode);
            }
        }
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

    private static void LogOutputCompletion(long elapsedMs)
    {
        MelonLogger.Msg(ConsoleColor.Green, $"- Saved! ({elapsedMs} ms)");
        MelonLogger.Msg("");
    }

    private async Task OutputScreenshot(CaptureParams captureParams)
    {
        MelonLogger.Msg("Capturing...");

        // Capture the entire screen (the screen can't be read at any other size, so the requested size is ignored)

        var width = Screen.width;
        var height = Screen.height;
        var screenshotTexture = new Texture2D(width, height);

        // Read screen contents into the texture

        screenshotTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        screenshotTexture.Apply();

        var bytes = Encode(screenshotTexture, captureParams.Quality, out var format);
        UnityEngine.Object.Destroy(screenshotTexture);

        // System.Windows.Forms isn't bundled with MelonLoader anymore :(

        //Clipboard.SetImage(
        //    (Bitmap)((new ImageConverter()).ConvertFrom(bytes))
        //);

        await OutputCapture(bytes, format, captureParams.OutputPath);
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
        PrepareCapture(waitTime, DelayedCapture);
        return;

        async void DelayedCapture()
        {
            if (AssetManager.CaptureStartAudio != null) AudioManager.Play(AssetManager.CaptureStartAudio);
            await OutputFisheyeCapture(captureParams);
            if (AssetManager.CaptureCompleteAudio != null) AudioManager.Play(AssetManager.CaptureCompleteAudio);
        }
    }

    public void PrepareScreenshotCapture(float waitTime, CaptureParams captureParams)
    {
        PrepareCapture(waitTime, DelayedCapture);
        return;

        async void DelayedCapture()
        {
            if (AssetManager.CaptureStartAudio != null) AudioManager.Play(AssetManager.CaptureStartAudio);
            await OutputScreenshot(captureParams);
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
