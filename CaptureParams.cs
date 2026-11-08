using UnityEngine;

namespace BoneSnap;

internal readonly record struct CaptureParams(Camera? Camera, int Width, int Height, int Quality, string OutputPath)
{
    public static CaptureParams FromPreferences(BoneSnapPreferences preferences, Camera? camera = null)
    {
        var capturePrefs = preferences.MainCapturePrefs;
        var resolutionScale = capturePrefs.ResolutionScale.Value;
        
        return new CaptureParams(
            Camera: camera,
            Width: (int)Math.Round(Screen.width * resolutionScale),
            Height: (int)Math.Round(Screen.height * resolutionScale),
            Quality: capturePrefs.CaptureQuality.Value,
            OutputPath: preferences.OutputPath.Value
        );
    }
}
