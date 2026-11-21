using UnityEngine;

namespace BoneSnap;

internal readonly record struct CaptureParams(
    Camera? Camera,
    int Width,
    int Height,
    int Quality,
    string OutputPath,
    bool DisablePostProcessing = false)
{
    public static CaptureParams FromPreferences(BoneSnapPreferences preferences, Camera? camera = null)
    {
        var (width, height) = GetResolution(preferences);

        return new CaptureParams(
            Camera: camera,
            Width: width,
            Height: height,
            Quality: preferences.MainCapturePrefs.CaptureQuality.Value,
            OutputPath: preferences.OutputPath.Value,
            DisablePostProcessing: preferences.DisablePostProcessing.Value
        );
    }

    public static (int Width, int Height) GetResolution(BoneSnapPreferences preferences)
    {
        var resolutionScale = preferences.MainCapturePrefs.ResolutionScale.Value;

        return Screenshotter.ClampToMaxTextureSize(
            (int)Math.Round(Screen.width * resolutionScale),
            (int)Math.Round(Screen.height * resolutionScale)
        );
    }
}
