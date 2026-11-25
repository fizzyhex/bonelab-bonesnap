using System.Diagnostics;
using BoneLib;
using BoneLib.BoneMenu;
using BoneSnap.Capture;
using BoneSnap.Extensions;
using BoneSnap.Menu.Pages;
using BoneSnap.Personalisation;
using BoneSnap.SDK;
using MelonLoader;
using UnityEngine;

namespace BoneSnap.Menu;

internal abstract class BoneMenu
{
    private const string GradientTitle =
        "<color=#FFFFFF>B</color><color=#ECE7F0>o</color><color=#D9D0E1>n</color><color=#C6B9D2>e</color><color=#B3A2C3>S</color><color=#C6B9D2>n</color><color=#D9D0E1>a</color><color=#ECE7F0>p</color>";

    private const float ExtremeThreshold = 2f;

    private static readonly Dictionary<(Page Parent, string Name), Page> ChildPages = new();
    private static readonly Dictionary<Page, FunctionElement> PreviewButtons = new();
    private static bool _watchingPreview;

    public static void DrawBoneMenu(BoneSnapPreferences preferences, Screenshotter screenshotter, Page? page=null)
    {
        if (page == null)
            page = Page.Root.CreatePage(GetTitle(), Color.white);
        else
            page.RemoveAll();

        page.CreateFunction(
            "Snap!",
            AccentColor.Current,
            () =>
            {
                var waitTime = preferences.MainCapturePrefs.CaptureDelay.Value;
                var captureParams = CaptureParams.FromPreferences(preferences);

                if (preferences.CaptureMode.Value == CaptureMode.Fisheye)
                    screenshotter.PrepareFisheyeCapture(waitTime, captureParams);
                else
                    screenshotter.PrepareScreenCapture(waitTime, captureParams);
            }
        );

        CreatePreviewButton(page, preferences, screenshotter);

        if (!Utilities.PlatformHelper.IsAndroid)
        {
            page.CreateEnum("Capture Mode", Color.white, preferences.CaptureMode.Value, value =>
            {
                preferences.CaptureMode.Value = (CaptureMode)value;
                preferences.ManualSave();
                DrawBoneMenu(preferences, screenshotter, page);

                // Switch the preview over to the new mode
                if (SnapHUD.IsPreviewing)
                    screenshotter.StartPreview(preferences);
            });
        }

        RenderCapturePrefs(preferences.MainCapturePrefs);

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

            var moreOptions = GetOrCreatePage(page, "More Options!");
            moreOptions.RemoveAll();

            CameraOverridesPage.Render(GetOrCreatePage(moreOptions, "Camera Overrides"), preferences, screenshotter);
            RigPage.Render(GetOrCreatePage(moreOptions, "Rig"), preferences);
            CompositingPage.Render(GetOrCreatePage(moreOptions, "Compositing"), preferences);

            var cloningPage = GetOrCreatePage(moreOptions, "Avatar Cloning");
            AvatarCloningPage.Render(cloningPage, preferences);
            AvatarCloningPage.Watch(() => AvatarCloningPage.Render(cloningPage, preferences));

            // Redraw the menu for anything reliant on accent for theming
            PersonalisationPage.Render(GetOrCreatePage(moreOptions, "Personalisation"), preferences,
                () => DrawBoneMenu(preferences, screenshotter, page));

            var snapCameraPage = GetOrCreatePage(moreOptions, "Snap Camera");
            SnapCameraPage.Render(snapCameraPage);
            SnapCameraPage.Watch(() => SnapCameraPage.Render(snapCameraPage));

            var cameraPersonalisationPage = GetOrCreatePage(snapCameraPage, "Personalisation");
            CameraPersonalisationPage.Render(cameraPersonalisationPage, preferences);
            CameraPersonalisationPage.Watch(cameraPersonalisationPage,
                () => CameraPersonalisationPage.Render(cameraPersonalisationPage, preferences));

            SteamPage.Render(GetOrCreatePage(moreOptions, "Steam"), preferences);

            moreOptions.CreateBool("Capture Preview", Color.white, preferences.ShowPreview.Value, value =>
            {
                preferences.ShowPreview.Value = value;
                preferences.ManualSave();
            });

            RenderOutputPath(moreOptions);
        }

        void RenderOutputPath(Page outputPage)
        {
            outputPage.CreateLabel($"Outputting to {preferences.OutputPath.Value}");

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
        }

        void RefreshTitle()
        {
            var newTitle = GetTitle();
            if (newTitle == page.Name) return;
            page.Name = newTitle;
            DrawBoneMenu(preferences, screenshotter, page);
            page.RefreshTitle();
        }

        string ExtremeText(string input) => AccentColor.Colorize(input);

        bool IsExtreme()
        {
            return preferences.MainCapturePrefs.ResolutionScale.Value >= ExtremeThreshold && preferences.CaptureMode.Value == CaptureMode.Fisheye;
        }

        string GetTitle() => IsExtreme() ? $"{GradientTitle} {ExtremeText("(EXTREME!!)")}" : GradientTitle;
    }

    /// <summary>
    /// Creates a button that toggles the capture preview
    /// </summary>
    internal static void CreatePreviewButton(Page page, BoneSnapPreferences preferences, Screenshotter screenshotter)
    {
        PreviewButtons[page] = page.CreateFunction(GetPreviewLabel(), Color.white, () =>
        {
            if (SnapHUD.IsPreviewing)
                screenshotter.StopPreview();
            else
                screenshotter.StartPreview(preferences);
        });

        if (_watchingPreview)
            return;

        _watchingPreview = true;

        SnapHUD.StateChanged += RefreshPreviewButtons;
        Hooking.OnLevelLoaded += _ => RefreshPreviewButtons();
    }

    private static string GetPreviewLabel() => SnapHUD.IsPreviewing ? "Stop Preview" : "Preview";

    private static void RefreshPreviewButtons()
    {
        var label = GetPreviewLabel();

        foreach (var button in PreviewButtons.Values)
        {
            if (button.ElementName != label)
                button.ElementName = label;
        }
    }

    internal const string PageGlyph = "<color=grey>›</color>";

    internal static Page GetOrCreatePage(Page parent, string name, string? title = null)
    {
        title ??= name;
        title = $"{title} {PageGlyph}";

        if (ChildPages.TryGetValue((parent, name), out var existing))
        {
            if (existing.Name != title)
            {
                existing.Name = title;
                existing.RefreshTitle();
            }

            if (!parent.TryGetChildPage(title, out _))
                parent.CreatePageLink(existing);

            return existing;
        }

        var created = parent.CreatePage(title, Color.white);
        ChildPages[(parent, name)] = created;
        return created;
    }
}
