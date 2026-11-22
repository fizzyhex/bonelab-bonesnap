using UnityEngine;

namespace BoneSnap.Capture;

public readonly record struct CaptureParams(
    Camera? Camera,
    int Width,
    int Height,
    int Quality,
    string OutputPath,
    IReadOnlySet<string>? DisabledVolumeComponents = null,
    bool ShiftHeadOffset = false,
    RevealMeshesMode RevealMeshes = RevealMeshesMode.None,
    HideRigsMode HideRigs = HideRigsMode.None,
    float? FieldOfView = null)
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
            HideRigs: preferences.HideRigs.Value,
            FieldOfView: preferences.FieldOfView.Value
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
