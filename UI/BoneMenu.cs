using System.Diagnostics;
using BoneLib.BoneMenu;
using BoneSnap.Extensions;
using MelonLoader;
using UnityEngine;

namespace BoneSnap.UI;

internal abstract class BoneMenu
{
    private const string GradientTitle =
        "<color=#FFFFFF>B</color><color=#ECE7F0>o</color><color=#D9D0E1>n</color><color=#C6B9D2>e</color><color=#B3A2C3>S</color><color=#C6B9D2>n</color><color=#D9D0E1>a</color><color=#ECE7F0>p</color>";

    private const float ExtremeThreshold = 2f;
    
    public static void DrawBoneMenu(BoneSnapPreferences preferences, Screenshotter screenshotter, Page? page=null)
    {
        if (page == null)
            page = Page.Root.CreatePage(GetTitle(), Color.white);
        else
            page.RemoveAll();
            
        page.CreateFunction(
            "Snap!",
            Color.cyan,
            () =>
            {
                var waitTime = preferences.MainCapturePrefs.CaptureDelay.Value;
                var captureParams = CaptureParams.FromPreferences(preferences);

                if (preferences.CaptureMode.Value == CaptureMode.Fisheye)
                    screenshotter.PrepareFisheyeCapture(waitTime, captureParams);
                else
                    screenshotter.PrepareScreenshotCapture(waitTime, captureParams);
            }
        );
        
        if (!Utilities.PlatformHelper.IsAndroid)
        {
            page.CreateEnum("Capture Mode", Color.white, preferences.CaptureMode.Value, value =>
            {
                preferences.CaptureMode.Value = (CaptureMode)value;
                preferences.ManualSave();
                DrawBoneMenu(preferences, screenshotter, page);
            });
        }
            
        RenderCapturePrefs(preferences.MainCapturePrefs);
        page.CreateFunction($"Outputting to {preferences.OutputPath.Value}", Color.white, null);

        page.CreateFunction("Open Folder", Color.white, () =>
        {
            if (screenshotter.LastCaptureFileName != null)
            {
                var path = Path.Combine(preferences.GetExplorerOutputPath(), screenshotter.LastCaptureFileName);
                MelonLogger.Msg($"path: {path}");
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                Process.Start("explorer.exe", preferences.GetExplorerOutputPath());
            }
        });
        
        return;
            
        void RenderCapturePrefs(CapturePrefs capturePrefs)
        {
            page.CreateInt("Capture Delay", Color.white, capturePrefs.CaptureDelay.Value, 1, 0, 60, delegate (int delay)
            {
                capturePrefs.CaptureDelay.Value = delay;
                preferences.ManualSave();
            });

            var quality = page.CreateInt("Capture Quality", Color.white, capturePrefs.CaptureQuality.Value, 5, 5, 100, delegate (int quality)
            {
                capturePrefs.CaptureQuality.Value = quality;
                preferences.ManualSave();
            });

            // https://docs.unity3d.com/6000.4/Documentation/ScriptReference/SystemInfo-maxTextureSize.html
            const int hardTextureLimit = 16384;
            
            quality.SetTooltip($"Unity has your max texture size set to: {SystemInfo.maxTextureSize}"
                               + (SystemInfo.maxTextureSize == hardTextureLimit ? " (which is the hard limit)." : ". To go higher, you would need more system memory.")
                               + $"\n\nCapture Device: {SystemInfo.graphicsDeviceName}.");

            if (preferences.CaptureMode.Value == CaptureMode.Fisheye)
            {
                page.CreateFloat(IsExtreme() ? ExtremeText("Render Scale") : "Render Scale", Color.white,
                    capturePrefs.ResolutionScale.Value, 0.1f, 0.1f, 8f, delegate(float scale)
                {
                    capturePrefs.ResolutionScale.Value = scale;
                    preferences.ManualSave();
                    RefreshTitle();
                });
            }
        }

        void RefreshTitle()
        {
            var newTitle = GetTitle();
            if (newTitle == page.Name) return;
            page.Name = newTitle;
            DrawBoneMenu(preferences, screenshotter, page);
            page.RefreshTitle();
        }
        
        string ExtremeText(string input)
        {
            return $"<color=#eb4034>{input}</color>";
        }
    
        bool IsExtreme()
        {
            return preferences.MainCapturePrefs.ResolutionScale.Value >= ExtremeThreshold && preferences.CaptureMode.Value == CaptureMode.Fisheye;
        }
    
        string GetTitle() => IsExtreme() ? $"{GradientTitle} {ExtremeText("(EXTREME!!)")}" : GradientTitle;
    }
}