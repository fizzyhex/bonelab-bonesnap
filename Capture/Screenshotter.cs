using System.Collections;
using System.Diagnostics;
using BoneSnap.Assets;
using BoneSnap.SDK;
using BoneSnap.Utilities;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using BoneLib;
using Il2CppSLZ.Marrow;
using MelonLoader;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using static BoneSnap.CoroutineUtils;

namespace BoneSnap.Capture;

/// <summary>
/// Runs captures and writes the results to disk.
/// <list type="bullet">
/// <item><c>Screenshotter.Camera.cs</c> renders a camera for fisheye and SDK
/// captures.</item>
/// <item><c>Screenshotter.Screen.cs</c> copies the game window for screen
/// captures.</item>
/// </list>
/// </summary>
internal partial class Screenshotter
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

    private readonly record struct RawCapture(Il2CppStructArray<byte> Pixels, GraphicsFormat Format, int Width, int Height, int Quality);

    /// <summary>
    /// Output of a <see cref="CaptureStep"/>. Coroutines can't return values.
    /// </summary>
    private sealed class CaptureResult
    {
        public RawCapture? Capture;
    }

    /// <summary>
    /// Coroutine that writes a capture to <paramref name="result"/>. <see cref="CaptureResult.Capture"/> stays null on failure.
    /// </summary>
    private delegate IEnumerator CaptureStep(CaptureParams captureParams, CaptureResult result);

    /// <summary>
    /// Shows a live preview of the next capture on the HUD.
    /// </summary>
    public void StartPreview(BoneSnapPreferences preferences)
    {
        if (preferences.CaptureMode.Value != CaptureMode.Fisheye)
        {
            SnapHUD.GetInstance().SwapState(new PreviewState(previewScreen: true));
            return;
        }

        var (camera, anchor) = GetFisheyePreviewSource();
        SnapHUD.GetInstance().SwapState(new PreviewState(camera, anchor, previewParams: () => CaptureParams.FromPreferences(preferences)));
    }

    public void StopPreview()
    {
        if (SnapHUD.IsPreviewing)
            SnapHUD.GetInstance().SwapState(new IdleState());
    }

    /// <summary>
    /// Plays the countdown audio if available, then starts <paramref name="capture"/> after <paramref
    /// name="waitTime"/>.
    /// </summary>
    private void StartCountdown(float waitTime, CaptureParams captureParams, CaptureStep capture)
    {
        if (AssetManager.CaptureTimerAudio != null)
            AudioManager.Play(AssetManager.CaptureTimerAudio);

        MelonCoroutines.Start(WaitAndThenRunCallback(waitTime, () =>
        {
            if (AssetManager.CaptureStartAudio != null) AudioManager.Play(AssetManager.CaptureStartAudio);

            MelonCoroutines.Start(RunCapture(captureParams, capture, OnCaptureFinished));
        }));

        return;

        static void OnCaptureFinished(Task output)
        {
            if (output.IsFaulted)
            {
                MelonLogger.Error($"Capture failed: {output.Exception}");
                return;
            }

            if (AssetManager.CaptureCompleteAudio != null) AudioManager.Play(AssetManager.CaptureCompleteAudio);
        }
    }

    /// <summary>
    /// Runs <paramref name="capture"/>, then encodes and writes the result on a worker thread.
    /// </summary>
    /// <param name="onFinished">Called on the main thread with the completed output task. Skipped if <paramref name="capture"/> produced no
    /// result.</param>
    private IEnumerator RunCapture(CaptureParams captureParams, CaptureStep capture, Action<Task> onFinished)
    {
        var result = new CaptureResult();
        var steps = WithHeadOffsetShifted(captureParams.ShiftHeadOffset, capture(captureParams, result));

        while (steps.MoveNext())
            yield return steps.Current;

        if (result.Capture is not { } rawCapture)
            yield break;

        // Poll the output task from the coroutine to keep the callback on the main
        // thread.
        var output = OutputCapture(rawCapture, captureParams.OutputPath);

        while (!output.IsCompleted)
            yield return null;

        onFinished(output);
    }

    /// <summary>
    /// Runs <paramref name="capture"/> with the local <see cref="PlayerAvatarArt"/> disabled when <paramref name="shiftHeadOffset"/> is set.
    /// </summary>
    private static IEnumerator WithHeadOffsetShifted(bool shiftHeadOffset, IEnumerator capture)
    {
        var disabledAvatarArt = shiftHeadOffset ? DisableLocalAvatarArt() : null;

        try
        {
            if (disabledAvatarArt != null)
                yield return null;

            while (capture.MoveNext())
                yield return capture.Current;
        }
        finally
        {
            if (disabledAvatarArt != null)
                disabledAvatarArt.enabled = true;
        }
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

    private static RawCapture ReadRawCapture(Texture2D texture, int quality)
        => new(texture.GetRawTextureData(), texture.graphicsFormat, texture.width, texture.height, quality);

    /// <summary>
    /// Reads <paramref name="source"/> into a new RGB24 texture. Main thread only.
    /// </summary>
    private static Texture2D ReadPixels(RenderTexture source)
    {
        var previousActiveTexture = RenderTexture.active;
        var texture = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);

        try
        {
            RenderTexture.active = source;
            texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
        }
        finally
        {
            RenderTexture.active = previousActiveTexture;
        }

        return texture;
    }

    private static string GetFormat(int quality) => quality == 100 ? "png" : "jpg";

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

        // Offset JPG quality by one increment so the encoder can reach 100
        // while the capture quality setting of 100 selects PNG.
        return GetFormat(capture.Quality) == "png"
            ? ImageConversion.EncodeArrayToPNG(pixels, capture.Format, width, height)
            : ImageConversion.EncodeArrayToJPG(pixels, capture.Format, width, height,
                quality: Mathf.Min(capture.Quality + CapturePrefs.CaptureQualityIncrement, 100));
    }

    private static async Task WriteToFileAsync(string path, byte[] imageData)
    {
        await using var stream = new FileStream(path, FileMode.Create);
        await stream.WriteAsync(imageData);
    }

    private static string GenerateTimecode(DateTime time)
    {
        var datestamp = time.ToString("yyyy-MM-dd");
        var timestamp = time.ToString("HH.mm.ss");

        return datestamp + " " + timestamp;
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
}
