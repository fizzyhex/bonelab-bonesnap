using System.Diagnostics;
using BoneLib;
using BoneLib.BoneMenu;
using BoneSnap.Capture;
using BoneSnap.Extensions;
using BoneSnap.Personalisation;
using MelonLoader;
using UnityEngine;

namespace BoneSnap.Menu;

internal abstract class BoneMenu
{
    private const string GradientTitle =
        "<color=#FFFFFF>B</color><color=#ECE7F0>o</color><color=#D9D0E1>n</color><color=#C6B9D2>e</color><color=#B3A2C3>S</color><color=#C6B9D2>n</color><color=#D9D0E1>a</color><color=#ECE7F0>p</color>";

    private const float ExtremeThreshold = 2f;

    private static bool _watching;
    private static Action? _renderPersonalisation;
    private static string? _adjustingBarcode;
    private static readonly Dictionary<(Page Parent, string Name), Page> ChildPages = new();
    
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
                    screenshotter.PrepareScreenCapture(waitTime, captureParams);
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

            RenderPostProcessingPrefs(GetOrCreatePage(moreOptions, "Post Processing"));
            RenderRigPrefs(GetOrCreatePage(moreOptions, "Rig"));
            
            moreOptions.CreateBool("Capture Preview", Color.white, preferences.ShowPreview.Value, value =>
            {
                preferences.ShowPreview.Value = value;
                preferences.ManualSave();
            });

            var personalisationPage = GetOrCreatePage(moreOptions, "Personalisation");
            RenderPersonalisation(personalisationPage);
            WatchPersonalisation(() => RenderPersonalisation(personalisationPage));

            RenderOutputPath(moreOptions);
        }

        void RenderPersonalisation(Page personalisationPage)
        {
            personalisationPage.RemoveAll();

            var accent = AccentColor.Current;
            var palette = preferences.AccentPalette.Value;

            personalisationPage.CreateDisplayEnum("Palette", Color.white, palette,
                [
                    (AccentPalette.CatppuccinFrappe, AccentColor.PaletteName(AccentPalette.CatppuccinFrappe)),
                    (AccentPalette.CatppuccinLatte, AccentColor.PaletteName(AccentPalette.CatppuccinLatte)),
                    (AccentPalette.Hex, AccentColor.PaletteName(AccentPalette.Hex))
                ],
                value =>
                {
                    preferences.AccentPalette.Value = value;
                    OnAccentChanged();
                });

            if (palette == AccentPalette.Hex)
            {
                var hex = personalisationPage.CreateString("Hex", accent, preferences.AccentHex.Value, value =>
                {
                    if (!AccentColor.TryParseHex(value, out var color))
                    {
                        // Leave the last good colour in place
                        RenderPersonalisation(personalisationPage);
                        return;
                    }

                    preferences.AccentHex.Value = AccentColor.ToHex(color);
                    OnAccentChanged();
                });

                hex.SetTooltip("Paste a hex colour, like #81C8BE or #FFF.");
            }
            else
            {
                var swatch = Enum.TryParse<CatppuccinAccent>(preferences.AccentSwatch.Value, out var parsed)
                    ? parsed
                    : Enum.Parse<CatppuccinAccent>(AccentColor.DefaultSwatch);

                personalisationPage.CreateEnum("Colour", accent, swatch, value =>
                {
                    preferences.AccentSwatch.Value = value.ToString();
                    OnAccentChanged();
                });
            }

            RenderKeychainPrefs(personalisationPage, accent);
        }

        void RenderKeychainPrefs(Page personalisationPage, Color accent)
        {
            var canChange = false;

            foreach (var hand in Player.HandsExist ? new[] { Player.LeftHand, Player.RightHand } : [])
            {
                if (!Trinkets.TryGetHeldCrate(hand, out var crate))
                    continue;

                canChange = true;
                var barcode = crate.Barcode.ID;

                personalisationPage.CreateFunction($"Change Keychain ({Trinkets.GetTitle(barcode)})", accent, () =>
                {
                    preferences.KeychainBarcode.Value = barcode;
                    preferences.ManualSave();
                });
            }

            var current = preferences.KeychainBarcode.Value;
            var nothingSelected = string.IsNullOrEmpty(current);
            
            if (nothingSelected && !canChange)
                personalisationPage.CreateLabel("Grab an item to select a custom keychain trinket!");

            if (nothingSelected)
                return;

            personalisationPage.CreateHeader($"Trinket - {Trinkets.GetTitle(current)}");

            var adjustPage = GetOrCreatePage(personalisationPage, "Adjust");

            if (current != _adjustingBarcode || adjustPage.Elements.Count == 0)
                RenderTrinketAdjust(adjustPage, current);

            personalisationPage.CreateFunction($"Remove", Color.white, () =>
            {
                preferences.KeychainBarcode.Value = string.Empty;
                preferences.ManualSave();
            });
        }

        void RenderTrinketAdjust(Page adjustPage, string barcode)
        {
            adjustPage.RemoveAll();
            _adjustingBarcode = barcode;

            var adjustment = Trinkets.GetAdjustment(preferences, barcode);

            void Save()
            {
                Trinkets.SetAdjustment(preferences, barcode, adjustment);
                preferences.ManualSave();
            }

            adjustPage.CreateLabel($"Adjusting {Trinkets.GetTitle(barcode)}");

            adjustPage.CreateFloat("Scale", Color.white, adjustment.Scale, .1f, .1f, 5f, value =>
            {
                adjustment.Scale = value;
                Save();
            });

            // Offsets are shown in centimetres, but stored in metres
            const float cm = 100f;

            adjustPage.CreateFloat("Offset X (cm)", Color.white, adjustment.OffsetX * cm, .5f, -10f, 10f, value =>
            {
                adjustment.OffsetX = value / cm;
                Save();
            });

            adjustPage.CreateFloat("Offset Y (cm)", Color.white, adjustment.OffsetY * cm, .5f, -10f, 10f, value =>
            {
                adjustment.OffsetY = value / cm;
                Save();
            });

            adjustPage.CreateFloat("Offset Z (cm)", Color.white, adjustment.OffsetZ * cm, .5f, -10f, 10f, value =>
            {
                adjustment.OffsetZ = value / cm;
                Save();
            });

            adjustPage.CreateInt("Rotation X", Color.white, (int)adjustment.RotationX, 15, -180, 180, value =>
            {
                adjustment.RotationX = value;
                Save();
            });

            adjustPage.CreateInt("Rotation Y", Color.white, (int)adjustment.RotationY, 15, -180, 180, value =>
            {
                adjustment.RotationY = value;
                Save();
            });

            adjustPage.CreateInt("Rotation Z", Color.white, (int)adjustment.RotationZ, 15, -180, 180, value =>
            {
                adjustment.RotationZ = value;
                Save();
            });

            adjustPage.CreateFunction("Reset", Color.white, () =>
            {
                Trinkets.ResetAdjustment(preferences, barcode);
                preferences.ManualSave();
                RenderTrinketAdjust(adjustPage, barcode);
            });
        }

        void OnAccentChanged()
        {
            preferences.ManualSave();
            // Redraw the menu for anything reliant on accent for theming
            DrawBoneMenu(preferences, screenshotter, page);
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

        void RenderRigPrefs(Page rigPage)
        {
            rigPage.RemoveAll();
            rigPage.CreateHeader("General");
            
            var hideRigs = rigPage.CreateDisplayEnum("Hide Rigs", Color.white, preferences.HideRigs.Value,
                [
                    (HideRigsMode.None, "None"),
                    (HideRigsMode.Local, "Local"),
                    (HideRigsMode.All, "All (Fusion)")
                ],
                value =>
                {
                    preferences.HideRigs.Value = value;
                    preferences.ManualSave();
                });

            hideRigs.SetTooltip("Hides RigManagers on the frame of capture.");

            rigPage.CreateHeader("Spawnables");
            
            var shiftHeadOffset = rigPage.CreateBool("Shift Head Offset", Color.white, preferences.ShiftHeadOffset.Value, value =>
            {
                preferences.ShiftHeadOffset.Value = value;
                preferences.ManualSave();
            });

            shiftHeadOffset.SetTooltip("Bonelab shifts your avatar's head back during gameplay to prevent it from clipping your view in VR. " +
                                       "This disables the PlayerAvatarArt script on your rig for the capture to fix that.");

            var revealMeshes = rigPage.CreateDisplayEnum("Reveal Meshes", Color.white, preferences.RevealMeshes.Value,
                [
                    (RevealMeshesMode.HeadAndHair, "Head & Hair"),
                    (RevealMeshesMode.Head, "Head"),
                    (RevealMeshesMode.Hair, "Hair"),
                    (RevealMeshesMode.None, "None")
                ],
                value =>
                {
                    preferences.RevealMeshes.Value = value;
                    preferences.ManualSave();
                });

            revealMeshes.SetTooltip("Bonelab also hides your avatar's head and hair during gameplay to prevent it from clipping your view in VR. " +
                                    "This shows the selected meshes on the frame the capture is taken.");
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
        
        string ExtremeText(string input) => AccentColor.Colorize(input);
    
        bool IsExtreme()
        {
            return preferences.MainCapturePrefs.ResolutionScale.Value >= ExtremeThreshold && preferences.CaptureMode.Value == CaptureMode.Fisheye;
        }
    
        string GetTitle() => IsExtreme() ? $"{GradientTitle} {ExtremeText("(EXTREME!!)")}" : GradientTitle;
    }

    private static Page GetOrCreatePage(Page parent, string name)
    {
        // Redraws remove the links BoneLib uses to find child pages.
        // Reuse the original page so players already viewing it see the updates.
        if (ChildPages.TryGetValue((parent, name), out var existing))
        {
            if (!parent.TryGetChildPage(name, out _))
                parent.CreatePageLink(existing);

            return existing;
        }

        var created = parent.CreatePage(name, Color.white);
        ChildPages[(parent, name)] = created;
        return created;
    }

    /// <summary>
    /// Keeps the Change Keychain buttons in sync with what the player is holding.
    /// </summary>
    private static void WatchPersonalisation(Action render)
    {
        _renderPersonalisation = render;

        if (_watching)
            return;

        _watching = true;

        Hooking.OnGrabObject += (_, _) => BeginRefresh();
        Hooking.OnReleaseObject += _ => BeginRefresh();
        Hooking.OnLevelLoaded += _ => BeginRefresh();

        return;
        
        void BeginRefresh() => CoroutineUtils.AfterFrame(RefreshPersonalisation);
    }

    public static void RefreshPersonalisation() => _renderPersonalisation?.Invoke();
}
