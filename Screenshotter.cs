using System.Diagnostics;
using BoneSnap.Assets;
using Il2CppOccaSoftware.Exposure.Runtime;
using Il2CppSLZ.Bonelab.SaveData;
using Il2CppSLZ.Marrow.SaveData;
using MelonLoader;
using UnityEngine;
using static BoneSnap.CoroutineUtils;

namespace BoneSnap;

internal static class Screenshotter
{
    private static int _screenshotIndex;
    private const float Megabyte = 1024f;
    public static string? LastCaptureFileName { get; private set; }

    private static string GenerateTimecode(DateTime time)
    {
        var datestamp = time.ToString("yyyy-MM-dd");
        var timestamp = time.ToString("HH.mm.ss");

        return datestamp + " " + timestamp;
    }

    private static async Task OutputFisheyeCapture(string outputPath, int quality = 100, float resolutionScale = 1)
    {
        MelonLogger.Msg("Capturing...");
        var spectatorReferences = ReferenceCache.GetSpectatorReferences();

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

        // Render scales other than 1 can cause the ambient occlusion to become offset
        var renderScale = GraphicsManager.graphicsSettings.RenderScale;
        GraphicsManager.graphicsSettings.RenderScale = 1;

        // Take capture

        var format = quality == 100 ? "png" : "jpg";
        var width = (int)Math.Round(Screen.width * resolutionScale);
        var height = (int)Math.Round(Screen.height * resolutionScale);

        var captureStopwatch = new Stopwatch();
        captureStopwatch.Start();

        var rt = new RenderTexture(width, height, 24);
        spectatorReferences.Camera.targetTexture = rt;

        var captureTexture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        spectatorReferences.Camera.Render();

        RenderTexture.active = rt;

        captureTexture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        captureTexture.Apply();

        spectatorReferences.Camera.targetTexture = null;
        RenderTexture.active = null;

        // Encode

        byte[] bytes = format == "png" ? captureTexture.EncodeToPNG() : captureTexture.EncodeToJPG(quality: Mathf.Min(quality + CapturePrefs.CaptureQualityIncrement, 100));

        captureStopwatch.Stop();
        MelonLogger.Msg(ConsoleColor.Green, $"- Taken! ({captureStopwatch.ElapsedMilliseconds} ms)");

        // Clean-up & remove any overrides

        RenderTexture.active = null;
        UnityEngine.Object.Destroy(rt);
        UnityEngine.Object.Destroy(captureTexture);

        spectatorReferences.RigScreenOptions.SetSpectatorCamera(spectatorCameraMode, spectatorReferences.RigScreenOptions.eyeTarget);

        if (autoExposureMode != null && spectatorReferences.AutoExposure != null)
        {
            spectatorReferences.AutoExposure.active = autoExposureActive;
            spectatorReferences.AutoExposure.mode = new AutoExposureModeParameter((AutoExposureMode)autoExposureMode);
        }

        GraphicsManager.graphicsSettings.RenderScale = renderScale;

        // Output to file

        var outputStopwatch = new Stopwatch();
        outputStopwatch.Start();

        _screenshotIndex++;
        Directory.CreateDirectory(outputPath);
        GenerateTimecode(DateTime.Now);
        var filePath = outputPath + $"/{NewCaptureName(format)}";

        LogFileSize(bytes, filePath);
        await WriteToFileAsync(filePath, bytes, () => LogOutputCompletion(outputStopwatch));
    }

    private static string NewCaptureName(string? format)
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

    private static void LogOutputCompletion(Stopwatch stopwatch)
    {
        stopwatch.Stop();
        MelonLogger.Msg(ConsoleColor.Green, $"- Saved! ({stopwatch.ElapsedMilliseconds} ms)");
        MelonLogger.Msg("");
    }

    private static async Task OutputScreenshot(string outputPath, int quality = 100)
    {
        MelonLogger.Msg("Capturing...");
        var format = quality == 100 ? "png" : "jpg";

        // Capture the entire screen

        var width = Screen.width;
        var height = Screen.height;
        var screenshotTexture = new Texture2D(width, height);

        // Read screen contents into the texture

        screenshotTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        screenshotTexture.Apply();

        // Encode texture into PNG

        byte[] bytes = format == "png" ? screenshotTexture.EncodeToPNG() : screenshotTexture.EncodeToJPG(quality: quality);
        UnityEngine.Object.Destroy(screenshotTexture);

        // Output to file

        var outputStopwatch = new Stopwatch();
        outputStopwatch.Start();

        _screenshotIndex++;
        Directory.CreateDirectory(outputPath);

        GenerateTimecode(DateTime.Now);
        var filePath = outputPath + $"/{NewCaptureName(format)}";

        await File.WriteAllBytesAsync(filePath, bytes);

        // System.Windows.Forms isn't bundled with MelonLoader anymore :(

        //Clipboard.SetImage(
        //    (Bitmap)((new ImageConverter()).ConvertFrom(bytes))
        //);

        MelonLogger.Msg($"Screenshot saved as {filePath}!");
    }

    private static async Task WriteToFileAsync(string path, byte[] imageData, Action? onComplete=null)
    {
        await using var stream = new FileStream(path, FileMode.Create);
        await stream.WriteAsync(imageData);
        onComplete?.Invoke();
    }

    private static void PrepareCapture(float waitTime, Action callback)
    {
        if (AssetManager.CaptureTimerAudio != null)
            AudioManager.Play(AssetManager.CaptureTimerAudio);

        MelonCoroutines.Start(WaitAndThenRunCallback(waitTime, callback));
    }

    public static void PrepareFisheyeCapture(float waitTime, int quality, string outputPath, float resolutionScale)
    {
        PrepareCapture(waitTime, DelayedCapture);
        return;

        async void DelayedCapture()
        {
            if (AssetManager.CaptureStartAudio != null) AudioManager.Play(AssetManager.CaptureStartAudio);
            await OutputFisheyeCapture(outputPath: outputPath, quality: quality, resolutionScale: resolutionScale);
            if (AssetManager.CaptureCompleteAudio != null) AudioManager.Play(AssetManager.CaptureCompleteAudio);
        }
    }

    public static void PrepareScreenshotCapture(float waitTime, int quality, string outputPath)
    {
        PrepareCapture(waitTime, DelayedCapture);
        return;

        async void DelayedCapture()
        {
            if (AssetManager.CaptureStartAudio != null) AudioManager.Play(AssetManager.CaptureStartAudio);
            await OutputScreenshot(outputPath: outputPath, quality: quality);
            if (AssetManager.CaptureCompleteAudio != null) AudioManager.Play(AssetManager.CaptureCompleteAudio);
        }
    }
}