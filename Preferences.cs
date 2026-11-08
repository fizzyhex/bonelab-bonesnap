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
    
public readonly struct BoneSnapPreferences(MelonPreferences_Category category)
{
    public readonly CapturePrefs MainCapturePrefs = new(category, "Main");
    //public readonly CapturePrefs BindCapturePrefs = new(category, "Bind");
    public readonly MelonPreferences_Entry<string> OutputPath = category.CreateEntry("OutputPath", "UserData/Screenshots");

    public readonly MelonPreferences_Entry<CaptureMode> CaptureMode =
        category.CreateEntry("CaptureMode", BoneSnap.CaptureMode.Screen);
        
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