using MelonLoader;
using MelonLoader.Utils;

namespace BoneSnap;

public readonly struct CapturePrefs(MelonPreferences_Category category, string prefix)
{
    public const int CaptureQualityIncrement = 5;
    public readonly MelonPreferences_Entry<int> CaptureDelay = category.CreateEntry($"{prefix}_CaptureDelay", 4);
    public readonly MelonPreferences_Entry<int> CaptureQuality = category.CreateEntry($"{prefix}_CaptureQuality", CaptureQualityIncrement);
    public readonly MelonPreferences_Entry<float> ResolutionScale = category.CreateEntry($"{prefix}_ResolutionScale", 1f);
}
    
public enum CaptureMode
{
    Screen,
    Fisheye
}
    
public enum HideRigsMode
{
    None,
    Local,
    All
}

[Flags]
public enum RevealMeshesMode
{
    None = 0,
    Head = 1,
    Hair = 2,
    HeadAndHair = Head | Hair
}

public enum PostProcessingPreset
{
    Fisheye,
    Minimal,
    MatchHeadset,
    Custom
}

public enum AccentPalette
{
    CatppuccinFrappe,
    CatppuccinLatte,
    Hex
}

public readonly record struct PostProcessingEffect(string Key, string DisplayName, string[] VolumeComponents)
{
    // Effects the spectator camera renders
    public static readonly PostProcessingEffect[] All =
    [
        new("Bloom", "Bloom", ["Bloom", "MKGlow"]),
        new("Tonemapping", "Tonemapping", ["Tonemapping"]),
        new("ColorGrading",
            "Color Grading",
            [
                "ColorAdjustments",
                "ColorCurves",
                "ColorLookup",
                "ChannelMixer",
                "LiftGammaGain",
                "ShadowsMidtonesHighlights",
                "SplitToning",
                "WhiteBalance"
            ]),
        new("ChromaticAberration", "Chromatic Aberration", ["ChromaticAberration"]),
        new("Volumetrics", "Volumetrics", ["Volumetrics"]),
        new("AutoExposure", "Auto Exposure", ["AutoExposure"]),
        new("LensDistortion", "Lens Distortion", ["LensDistortion"]),
        new("AmbientOcclusion", "Ambient Occlusion", ["HBAO"])
    ];
}

public readonly struct BoneSnapPreferences(MelonPreferences_Category category)
{
    public readonly CapturePrefs MainCapturePrefs = new(category, "Main");
    //public readonly CapturePrefs BindCapturePrefs = new(category, "Bind");
    public readonly MelonPreferences_Entry<string> OutputPath = category.CreateEntry("OutputPath", "UserData/Screenshots");

    public readonly MelonPreferences_Entry<CaptureMode> CaptureMode =
        category.CreateEntry("CaptureMode", BoneSnap.CaptureMode.Screen);

    public readonly MelonPreferences_Entry<PostProcessingPreset> PostProcessingPreset =
        category.CreateEntry("PostProcessingPreset", BoneSnap.PostProcessingPreset.Minimal);

    public readonly MelonPreferences_Entry<bool> ShiftHeadOffset = category.CreateEntry("ShiftHeadOffset", true);

    public readonly MelonPreferences_Entry<RevealMeshesMode> RevealMeshes =
        category.CreateEntry("RevealMeshes", RevealMeshesMode.HeadAndHair);

    public readonly MelonPreferences_Entry<HideRigsMode> HideRigs =
        category.CreateEntry("HideRigs", HideRigsMode.None);

    public readonly MelonPreferences_Entry<bool> ShowPreview = category.CreateEntry("ShowPreview", true);

    // The spectator camera's field of view in fisheye mode
    public const float DefaultFieldOfView = 78f;

    public readonly MelonPreferences_Entry<float> FieldOfView = category.CreateEntry("FieldOfView", DefaultFieldOfView);

    public readonly MelonPreferences_Entry<AccentPalette> AccentPalette =
        category.CreateEntry("AccentPalette", BoneSnap.AccentPalette.CatppuccinFrappe);

    public readonly MelonPreferences_Entry<string> AccentSwatch = category.CreateEntry("AccentSwatch", Personalisation.AccentColor.DefaultSwatch);

    public readonly MelonPreferences_Entry<string> AccentHex = category.CreateEntry("AccentHex", "#81C8BE");

    public readonly MelonPreferences_Entry<string> KeychainBarcode = category.CreateEntry("KeychainBarcode", string.Empty);

    public readonly MelonPreferences_Entry<string> KeychainAdjustments = category.CreateEntry("KeychainAdjustments", "{}");

    // Only used by the Advanced preset
    public readonly Dictionary<string, MelonPreferences_Entry<bool>> PostProcessingEffects =
        CreatePostProcessingEntries(category);

    public bool IsEffectEnabled(PostProcessingEffect effect) => PostProcessingPreset.Value switch
    {
        BoneSnap.PostProcessingPreset.Minimal => effect.Key is not "LensDistortion",
        BoneSnap.PostProcessingPreset.MatchHeadset => effect.Key is not ("LensDistortion" or "ChromaticAberration" or "AutoExposure"),
        BoneSnap.PostProcessingPreset.Custom => PostProcessingEffects[effect.Key].Value,
        _ => true
    };
    
    public HashSet<string> GetDisabledVolumeComponents()
    {
        var preferences = this;
        return PostProcessingEffect.All
            .Where(effect => !preferences.IsEffectEnabled(effect))
            .SelectMany(effect => effect.VolumeComponents)
            .ToHashSet();
    }

    private static Dictionary<string, MelonPreferences_Entry<bool>> CreatePostProcessingEntries(MelonPreferences_Category category)
        => PostProcessingEffect.All.ToDictionary(
            effect => effect.Key,
            effect => category.CreateEntry($"PostProcessing_{effect.Key}", true));

    public string GetExplorerOutputPath()
    {
        var path = OutputPath.Value.StartsWith("UserData/")
            ? $"{MelonEnvironment.UserDataDirectory}/{OutputPath.Value[9..]}"
            : OutputPath.Value;

        return path.Replace("/", "\\");
    }

    public void ManualSave()
    {
        category.SaveToFile();
    }
}