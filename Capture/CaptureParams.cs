using UnityEngine;

namespace BoneSnap.Capture;

public enum CompositingLayer
{
    /// Default - keeps the camera's current layer mask.
    Combined,
    /// Captures just the background with players & held items removed.
    Background,
    /// Captures players & their held items.
    PlayerMask,
    /// Captures players & their held items - masked out from the combined layer to retain shadows from the scene.
    PlayerCutout
}

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
    float? FieldOfView = null,
    CompositingMode CompositingMode = CompositingMode.Combined,
    CompositingLayer CompositingLayer = CompositingLayer.Combined
    )
{
    public static CaptureParams FromPreferences(BoneSnapPreferences preferences,
        Camera? camera = null,
        CompositingLayer compositingLayer = CompositingLayer.Combined)
    {
        var (width, height) = GetResolution(preferences);
        var overrides = preferences.CameraOverrides.Value;

        return new CaptureParams(
            Camera: camera,
            Width: width,
            Height: height,
            // .jpg doesn't support transparency. We should move to webp in the future though.
            Quality: compositingLayer == CompositingLayer.Combined ? preferences.MainCapturePrefs.CaptureQuality.Value : 100,
            OutputPath: preferences.OutputPath.Value,
            DisabledVolumeComponents: overrides ? preferences.GetDisabledVolumeComponents() : null,
            HideRigs: preferences.HideRigs.Value,
            FieldOfView: overrides ? preferences.FieldOfView.Value : null,
            CompositingMode: preferences.CompositingMode.Value,
            CompositingLayer: compositingLayer
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
