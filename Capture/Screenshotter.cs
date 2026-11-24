using System.Collections;
using System.Diagnostics;
using BoneSnap.Assets;
using BoneSnap.SDK;
using BoneSnap.Utilities;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSLZ.Marrow;
using MelonLoader;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using static BoneSnap.CoroutineUtils;

namespace BoneSnap.Capture;

/// <summary>
/// Responsible for taking/encoding snaps.
/// <list type="bullet">
/// <item><c>Screenshotter.Camera.cs</c>: Fisheye and SDK Captures</item>
/// <item><c>Screenshotter.Screen.cs</c>: Screen Captures</item>
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

    private readonly record struct RawCapture(
        Il2CppStructArray<byte> Pixels,
        GraphicsFormat Format,
        int Width,
        int Height,
        int Quality,
        CompositingLayer Layer = Capture.CompositingLayer.Combined,
        // RGB24 colour to combine with this capture's alpha when encoding
        Il2CppStructArray<byte>? ColorPixels = null);

    // Encodes can hang when they overlap on attached threads
    private static readonly object EncodeLock = new();

    private sealed class CaptureResult
    {
        public RawCapture[]? Captures;
    }

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

            if (AssetManager.CaptureCompleteAudio != null) AudioManager.Play(AssetManager.CaptureCompleteAudio, 0.35f);
        }
    }

    /// <summary>
    /// Runs capture, and encodes/writes the result on another thread.
    /// </summary>
    private IEnumerator RunCapture(CaptureParams captureParams, CaptureStep capture, Action<Task> onFinished)
    {
        var result = new CaptureResult();

        using (captureParams.ShiftHeadOffset ? HeadOffset.Suppress() : null)
        {
            var steps = capture(captureParams, result);

            while (steps.MoveNext())
                yield return steps.Current;
        }

        if (result.Captures is not { Length: > 0 } rawCaptures)
            yield break;

        // poll the output task from the coroutine to keep the callback on the main
        // thread
        var output = OutputCaptures(rawCaptures, captureParams.OutputPath);

        while (!output.IsCompleted)
            yield return null;

        onFinished(output);
    }
    
    private static RawCapture ReadRawCapture(Texture2D texture, int quality,
        CompositingLayer layer = Capture.CompositingLayer.Combined)
        => new(texture.GetRawTextureData(), texture.graphicsFormat, texture.width, texture.height, quality, layer);

    /// <summary>
    /// Reads <paramref name="source"/> into a new RGB24 texture. Main thread only.
    /// </summary>
    private static Texture2D ReadPixels(RenderTexture source, bool transparent=false)
    {
        var previousActiveTexture = RenderTexture.active;
        var texture = new Texture2D(source.width, source.height, transparent ? TextureFormat.RGBA32 : TextureFormat.RGB24, false);

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
    /// Encodes and writes <paramref name="captures"/> in the background as one batch. Call from the main thread!
    /// </summary>
    private Task OutputCaptures(RawCapture[] captures, string outputPath)
    {
        Directory.CreateDirectory(outputPath);

        // Passes share a name so they sort together, e.g. "... 5 Background.png"
        var baseName = NewCaptureName();
        var suffixLayers = captures.Length > 1;

        var outputs = captures
            .Select(capture => (capture, Path: $"{outputPath}/{GetFileName(baseName, capture, suffixLayers)}"))
            .ToArray();

        LastCaptureFileName = Path.GetFileName(outputs[^1].Path);
        var output = EncodeAndWriteAsync(outputs);

        SnapHUD.GetInstance().SwapState(new EncodingInProgressState());
        MelonCoroutines.Start(WaitForTaskAndThenRunCallback(output, OnOutputFinished));

        return output;

        void OnOutputFinished(Task task)
        {
            SnapHUD.GetInstance().SwapState(task.IsCompletedSuccessfully
                ? new SaveCompleteState(suffixLayers ? $"{baseName} (x{captures.Length})" : LastCaptureFileName,
                    ((Task<long>)task).Result)
                : new IdleState());
        }
    }

    private static Il2CppStructArray<byte> ApplyAlphaMask(Il2CppStructArray<byte> sourceArray,
        Il2CppStructArray<byte> maskArray, int width, int height)
    {
        // ┌────────┬─────────────────┬─────────────────────────────────────┐
        // │ Format │ Bytes per pixel │ Byte offsets for pixel i            │
        // ├────────┼─────────────────┼─────────────────────────────────────┤
        // │ RGB24  │        3        │ R=i*3, G=i*3+1, B=i*3+2             │
        // │ RGBA32 │        4        │ R=i*4, G=i*4+1, B=i*4+2, A=i*4+3    │
        // └────────┴─────────────────┴─────────────────────────────────────┘
        // Use spans to avoid going through interop for each index.
        
        var source = sourceArray.AsSpan();
        var mask = maskArray.AsSpan();

        var output = new Il2CppStructArray<byte>(width * height * 4); // include alpha channel
        var dst = output.AsSpan();

        for (var i = 0; i < width * height; i++)
        {
            // colour from the combined pass
            dst[i * 4] = source[i * 3];
            dst[i * 4 + 1] = source[i * 3 + 1];
            dst[i * 4 + 2] = source[i * 3 + 2];
            // alpha from the mask
            dst[i * 4 + 3] = mask[i * 4 + 3]; // (byte)(mask[i * 4 + 3] > 0 ? 255 : 0);
        }

        return output;
    }

    /// <summary>
    /// Encodes and writes each capture in turn off the main thread.
    /// </summary>
    private static Task<long> EncodeAndWriteAsync((RawCapture Capture, string Path)[] outputs)
    {
        return Task.Factory.StartNew(() =>
        {
            lock (EncodeLock)
            {
                var thread = IL2CPP.il2cpp_thread_attach(IL2CPP.il2cpp_domain_get());

                try
                {
                    long totalEncodeMs = 0;

                    foreach (var (capture, path) in outputs)
                    {
                        // TODO: decide what happens when one pass fails
                        var stopwatch = Stopwatch.StartNew();

                        byte[] bytes;

                        var colorPixels = capture.Layer == Capture.CompositingLayer.PlayerCutout
                            ? outputs.First(x => x.Capture.Layer == Capture.CompositingLayer.Combined).Capture.Pixels
                            : capture.ColorPixels;

                        if (colorPixels != null)
                        {
                            bytes = Encode(capture,
                                ApplyAlphaMask(colorPixels, capture.Pixels, capture.Width, capture.Height));
                        }
                        else
                        {
                            bytes = Encode(capture, capture.Pixels);
                        }

                        var encodeMs = stopwatch.ElapsedMilliseconds;

                        LogFileSize(bytes, path);
                        File.WriteAllBytes(path, bytes);
                        LogOutputCompletion(encodeMs, stopwatch.ElapsedMilliseconds - encodeMs);

                        totalEncodeMs += encodeMs;
                    }

                    return totalEncodeMs;
                }
                finally
                {
                    IL2CPP.il2cpp_thread_detach(thread);
                }
            }
        }, TaskCreationOptions.LongRunning);
    }

    private static byte[] Encode(RawCapture capture, Il2CppStructArray<byte> bytes)
    {
        var pixels = new Il2CppSystem.Array(bytes.Pointer);
        var width = (uint)capture.Width;
        var height = (uint)capture.Height;

        var output = GetFormat(capture.Quality) == "png"
            ? ImageConversion.EncodeArrayToPNG(pixels, capture.Format, width, height)
            : ImageConversion.EncodeArrayToJPG(pixels, capture.Format, width, height,
                quality: Mathf.Min(capture.Quality + CapturePrefs.CaptureQualityIncrement, 100));
        
        GC.KeepAlive(bytes); // JIT, please don't snatch our shit.
        return output;
    }

    private static string GenerateTimecode(DateTime time)
    {
        var datestamp = time.ToString("yyyy-MM-dd");
        var timestamp = time.ToString("HH.mm.ss");

        return datestamp + " " + timestamp;
    }

    private string NewCaptureName()
    {
        var timecode = GenerateTimecode(DateTime.Now);
        _screenshotIndex++;

        return $"{timecode} {_screenshotIndex}";
    }

    private static string GetFileName(string baseName, RawCapture capture, bool suffixLayer)
        => (suffixLayer ? $"{baseName} {capture.Layer}" : baseName) + "." + GetFormat(capture.Quality);

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
