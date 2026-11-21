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

                // Post processing & rig overrides only apply to the spectator camera (fisheye)
                var moreOptions = page.CreatePage("More Options!", Color.white);
                RenderPostProcessingPrefs(moreOptions.CreatePage("Post Processing", Color.white));
                RenderRigPrefs(moreOptions.CreatePage("Rig", Color.white));

                moreOptions.CreateBool("Show Preview", Color.white, preferences.ShowPreview.Value, value =>
                {
                    preferences.ShowPreview.Value = value;
                    preferences.ManualSave();
                });

                RenderOutputPath(moreOptions);
            }
            else
            {
                RenderOutputPath(page);
            }
        }

        void RenderOutputPath(Page outputPage)
            => outputPage.CreateFunction($"Outputting to {preferences.OutputPath.Value}", Color.gray, null);

        void RenderRigPrefs(Page rigPage)
        {
            var shiftHeadOffset = rigPage.CreateBool("Shift Head Offset", Color.white, preferences.ShiftHeadOffset.Value, value =>
            {
                preferences.ShiftHeadOffset.Value = value;
                preferences.ManualSave();
            });

            shiftHeadOffset.SetTooltip("Bonelab shifts your avatar's head back during gameplay to prevent it from clipping your view in VR. " +
                                       "This disables the PlayerAvatarArt script on your rig on the frame the capture is taken to fix that.");

            // A function rather than an enum, as enum elements can only show the raw value names
            FunctionElement? hideRigs = null;
            hideRigs = rigPage.CreateFunction(GetHideRigsName(), Color.white, () =>
            {
                preferences.HideRigs.Value = preferences.HideRigs.Value switch
                {
                    HideRigsMode.None => HideRigsMode.Local,
                    HideRigsMode.Local => HideRigsMode.All,
                    _ => HideRigsMode.None
                };
                preferences.ManualSave();
                hideRigs!.ElementName = GetHideRigsName();
            });

            hideRigs.SetTooltip("Hides RigManagers on the frame of capture.");

            string GetHideRigsName() => "Hide Rigs: " + preferences.HideRigs.Value switch
            {
                HideRigsMode.Local => "Local",
                HideRigsMode.All => "All (Fusion)",
                _ => "None"
            };
        }

        void RenderPostProcessingPrefs(Page postProcessingPage)
        {
            postProcessingPage.RemoveAll();

            var preset = postProcessingPage.CreateEnum("Preset", Color.white, preferences.PostProcessingPreset.Value, value =>
            {
                preferences.PostProcessingPreset.Value = (PostProcessingPreset)value;
                preferences.ManualSave();
                RenderPostProcessingPrefs(postProcessingPage);
            });

            preset.SetTooltip(preferences.PostProcessingPreset.Value switch
            {
                PostProcessingPreset.Minimal => "All effects off, excluding Chromatic Aberration.",
                PostProcessingPreset.None => "No post processing.",
                PostProcessingPreset.Advanced => "Tweak individual settings, if you want that for some reason I guess!",
                _ => "Default look of Bonelab's spectator camera."
            });

            if (preferences.PostProcessingPreset.Value != PostProcessingPreset.Advanced)
                return;

            foreach (var effect in PostProcessingEffect.All)
            {
                var entry = preferences.PostProcessingEffects[effect.Key];
                postProcessingPage.CreateBool(effect.DisplayName, Color.white, entry.Value, value =>
                {
                    entry.Value = value;
                    preferences.ManualSave();
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