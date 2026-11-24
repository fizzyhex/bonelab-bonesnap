using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace BoneSnap.Capture;

internal partial class Screenshotter
{
    /// <summary>
    /// Responsible for sending captures to the Steam screenshot library.
    /// </summary>
    private sealed class SteamExport
    {
        // Steam screenshots are saved as `.jpg`, so we use a Steam flavoured background colour that blends better
        // with the client.
        private static readonly Color32 Background = new(37, 42, 51, 255);

        private const int JpgQuality = 95;

        private readonly int _index;
        private readonly string _fileName;

        private RawCapture _capture;
        private string? _path;

        private SteamExport(int index, string fileName)
        {
            _index = index;
            _fileName = fileName;
        }

        public static SteamExport? Create(RawCapture[] captures, CaptureParams captureParams, string baseName)
        {
            if (!captureParams.SaveToSteam)
                return null;

            var index = SelectOutput(captures, captureParams.SteamLayer);
            return index != -1 ? new SteamExport(index, $"{baseName} Steam.jpg") : null;
        }

        /// <summary>
        /// Called on the encode thread after each capture is written.
        /// </summary>
        public void OnEncoded(int index, RawCapture capture, Il2CppStructArray<byte> pixels, string path)
        {
            if (index != _index)
                return;

            _capture = capture;
            _path = WriteCopy(capture, pixels, path, _fileName);
        }

        /// <summary>
        /// Hands the capture to Steam. Main thread only.
        /// </summary>
        public bool Submit()
            => _path != null && SteamScreenshotLibrary.Add(Path.GetFullPath(_path), _capture.Width, _capture.Height);
        
        private static int SelectOutput(RawCapture[] captures, SteamLayer steamLayer)
        {
            var index = Array.FindIndex(captures, capture => steamLayer switch
            {
                SteamLayer.Players => IsPlayerLayer(capture.Layer),
                SteamLayer.Background => capture.Layer == Capture.CompositingLayer.Background,
                _ => capture.Layer == Capture.CompositingLayer.Combined
            });

            return index != -1 ? index : Array.FindIndex(captures, capture => capture.Layer == Capture.CompositingLayer.Combined);
        }
        
        private static string WriteCopy(RawCapture capture, Il2CppStructArray<byte> pixels, string path, string fileName)
        {
            // RGBA32 - see ApplyAlphaMask
            if (pixels.Length != capture.Width * capture.Height * 4)
                return path;

            var directory = Path.Combine(Path.GetTempPath(), "BoneSnap", "Steam");
            Directory.CreateDirectory(directory);
            DeleteStaleCopies(directory);

            var flattened = new Il2CppStructArray<byte>(pixels.Length);
            var src = pixels.AsSpan();
            var dst = flattened.AsSpan();

            for (var i = 0; i < src.Length; i += 4)
            {
                var alpha = src[i + 3];
                dst[i] = Blend(src[i], Background.r, alpha);
                dst[i + 1] = Blend(src[i + 1], Background.g, alpha);
                dst[i + 2] = Blend(src[i + 2], Background.b, alpha);
                dst[i + 3] = 255;
            }

            var steamPath = Path.Combine(directory, fileName);
            File.WriteAllBytes(steamPath, Encode(capture, flattened, jpgQuality: JpgQuality));
            return steamPath;

            static byte Blend(byte color, byte background, byte alpha)
                => (byte)((color * alpha + background * (255 - alpha) + 127) / 255);
        }
        
        private static void DeleteStaleCopies(string directory)
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
                    // steam may still be reading it
                }
            }
        }
    }
}
