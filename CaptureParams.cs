using UnityEngine;

namespace BoneSnap;

internal readonly record struct CaptureParams(
    Camera? Camera,
    int Width,
    int Height,
    int Quality,
    string OutputPath,
    IReadOnlySet<string>? DisabledVolumeComponents = null,
    bool ShiftHeadOffset = false,
    HideRigsMode HideRigs = HideRigsMode.None)
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
            DisabledVolumeComponents: preferences.GetDisabledVolumeComponents(),
            ShiftHeadOffset: preferences.ShiftHeadOffset.Value,
            HideRigs: preferences.HideRigs.Value
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
