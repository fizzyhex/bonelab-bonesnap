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

    // #252A33 - transparent captures are flattened onto this for Steam
    private static readonly Color32 SteamBackground = new(37, 42, 51, 255);
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

        static void OnCaptureFinished(Task<bool> output)
        {
            if (output.IsFaulted)
            {
                MelonLogger.Error($"Capture failed: {output.Exception}");
                return;
            }

            // Steam plays its own capture sound :P
            var savedToSteam = output.IsCompletedSuccessfully && output.Result;

            if (!savedToSteam && AssetManager.CaptureCompleteAudio != null)
                AudioManager.Play(AssetManager.CaptureCompleteAudio, 0.35f);
        }
    }

    /// <summary>
    /// Runs capture, and encodes/writes the result on another thread.
    /// </summary>
    private IEnumerator RunCapture(CaptureParams captureParams, CaptureStep capture, Action<Task<bool>> onFinished)
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
        var output = OutputCaptures(rawCaptures, captureParams.OutputPath,
            captureParams.SaveToSteam ? captureParams.SteamLayer : null);

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
    /// <param name="steamLayer">The pass to send to Steam, or null to skip Steam.</param>
    /// <returns>If Steam is handling the capture.</returns>
    private Task<bool> OutputCaptures(RawCapture[] captures, string outputPath, SteamLayer? steamLayer)
    {
        Directory.CreateDirectory(outputPath);

        var baseName = NewCaptureName();
        var suffixLayers = captures.Length > 1;

        var outputs = captures
            .Select(capture => (capture, Path: $"{outputPath}/{GetFileName(baseName, capture, suffixLayers)}"))
            .ToArray();

        LastCaptureFileName = Path.GetFileName(outputs[^1].Path);

        var steamIndex = steamLayer is { } layer ? SelectSteamOutput(captures, layer) : -1;
        var output = EncodeAndWriteAsync(outputs, steamIndex, $"{baseName} Steam.jpg");

        SnapHUD.GetInstance().SwapState(new EncodingInProgressState());
        // Completes after the Steam hand-off, which has to happen on the main thread
        var finished = new TaskCompletionSource<bool>();
        MelonCoroutines.Start(WaitForTaskAndThenRunCallback(output, OnOutputFinished));

        return finished.Task;

        void OnOutputFinished(Task task)
        {
            var savedToSteam = false;
            var result = task.IsCompletedSuccessfully ? ((Task<EncodeResult>)task).Result : default;

            if (result.SteamPath != null)
            {
                var steamCapture = captures[steamIndex];
                savedToSteam = SteamScreenshotLibrary.Add(Path.GetFullPath(result.SteamPath), steamCapture.Width, steamCapture.Height);
            }

            SnapHUD.GetInstance().SwapState(task.IsCompletedSuccessfully
                ? new SaveCompleteState(suffixLayers ? $"{baseName} (x{captures.Length})" : LastCaptureFileName,
                    result.EncodeMs)
                : new IdleState());

            if (task.IsFaulted)
                finished.SetException(task.Exception!.InnerExceptions);
            else if (task.IsCanceled)
                finished.SetCanceled();
            else
                finished.SetResult(savedToSteam);
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
    /// Picks a single layer to send to Steam to avoid cluttering the screenshots folder.
    /// Combined captures only have the one pass, so fall back to it.
    /// </summary>
    private static int SelectSteamOutput(RawCapture[] captures, SteamLayer steamLayer)
    {
        var index = Array.FindIndex(captures, capture => steamLayer switch
        {
            SteamLayer.Players => IsPlayerLayer(capture.Layer),
            SteamLayer.Background => capture.Layer == Capture.CompositingLayer.Background,
            _ => capture.Layer == Capture.CompositingLayer.Combined
        });

        return index != -1 ? index : Array.FindIndex(captures, capture => capture.Layer == Capture.CompositingLayer.Combined);
    }

    /// <param name="EncodeMs">Total time spent encoding.</param>
    /// <param name="SteamPath">File to hand to Steam, if a capture was picked for it.</param>
    private readonly record struct EncodeResult(long EncodeMs, string? SteamPath);

    /// <summary>
    /// Encodes and writes each capture in turn off the main thread.
    /// </summary>
    /// <param name="steamIndex">Index of the capture going to Steam, or -1.</param>
    /// <param name="steamFileName">Name for the flattened copy of a transparent Steam capture.</param>
    private static Task<EncodeResult> EncodeAndWriteAsync((RawCapture Capture, string Path)[] outputs,
        int steamIndex, string steamFileName)
    {
        return Task.Factory.StartNew(() =>
        {
            lock (EncodeLock)
            {
                var thread = IL2CPP.il2cpp_thread_attach(IL2CPP.il2cpp_domain_get());

                try
                {
                    long totalEncodeMs = 0;
                    string? steamPath = null;

                    for (var i = 0; i < outputs.Length; i++)
                    {
                        var (capture, path) = outputs[i];
                        var stopwatch = Stopwatch.StartNew();

                        var colorPixels = capture.Layer == Capture.CompositingLayer.PlayerCutout
                            ? outputs.First(x => x.Capture.Layer == Capture.CompositingLayer.Combined).Capture.Pixels
                            : capture.ColorPixels;

                        var pixels = colorPixels != null
                            ? ApplyAlphaMask(colorPixels, capture.Pixels, capture.Width, capture.Height)
                            : capture.Pixels;

                        var bytes = Encode(capture, pixels);
                        var encodeMs = stopwatch.ElapsedMilliseconds;

                        LogFileSize(bytes, path);
                        File.WriteAllBytes(path, bytes);
                        LogOutputCompletion(encodeMs, stopwatch.ElapsedMilliseconds - encodeMs);

                        totalEncodeMs += encodeMs;

                        if (i == steamIndex)
                            steamPath = WriteSteamCopy(capture, pixels, path, steamFileName);
                    }

                    return new EncodeResult(totalEncodeMs, steamPath);
                }
                finally
                {
                    IL2CPP.il2cpp_thread_detach(thread);
                }
            }
        }, TaskCreationOptions.LongRunning);
    }

    /// <summary>
    /// Steam saves screenshots as .jpg and fills transparency with white, so transparent captures get an
    /// opaque copy over <see cref="SteamBackground"/> for Steam to import instead.
    /// </summary>
    /// <returns>The path to hand to Steam.</returns>
    private static string WriteSteamCopy(RawCapture capture, Il2CppStructArray<byte> pixels, string path, string fileName)
    {
        // RGBA32 - see ApplyAlphaMask
        if (pixels.Length != capture.Width * capture.Height * 4)
            return path;

        var directory = Path.Combine(Path.GetTempPath(), "BoneSnap", "Steam");
        Directory.CreateDirectory(directory);
        DeleteStaleSteamCopies(directory);

        var flattened = new Il2CppStructArray<byte>(pixels.Length);
        var src = pixels.AsSpan();
        var dst = flattened.AsSpan();

        for (var i = 0; i < src.Length; i += 4)
        {
            var alpha = src[i + 3];
            dst[i] = Blend(src[i], SteamBackground.r, alpha);
            dst[i + 1] = Blend(src[i + 1], SteamBackground.g, alpha);
            dst[i + 2] = Blend(src[i + 2], SteamBackground.b, alpha);
            dst[i + 3] = 255;
        }

        // Steam stores .jpg files as-is, so this is the quality that ends up in the library
        var steamPath = Path.Combine(directory, fileName);
        File.WriteAllBytes(steamPath, Encode(capture with { Quality = 95 }, flattened));
        return steamPath;

        static byte Blend(byte color, byte background, byte alpha)
            => (byte)((color * alpha + background * (255 - alpha) + 127) / 255);
    }

    /// <summary>
    /// Steam copies screenshots in on its own time, so copies are cleared out on a later capture.
    /// </summary>
    private static void DeleteStaleSteamCopies(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            try
            {
                if (DateTime.Now - File.GetLastWriteTime(file) > TimeSpan.FromMinutes(1))
                    File.Delete(file);
            }
            catch (IOException)
            {
                // Steam may still be reading it
            }
        }
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
