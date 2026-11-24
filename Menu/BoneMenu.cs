using System.Diagnostics;
using BoneLib;
using BoneLib.BoneMenu;
using BoneSnap.Capture;
using BoneSnap.Cloning;
using BoneSnap.Extensions;
using BoneSnap.Multiplayer;
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

    private static bool _watching;
    private static Action? _renderPersonalisation;
    private static Page? _personalisationPage;
    private static Action? _renderCloning;
    private static string? _adjustingBarcode;
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

            RenderCameraOverrides(GetOrCreatePage(moreOptions, "Camera Overrides"));
            RenderRigPrefs(GetOrCreatePage(moreOptions, "Rig"));
            RenderCompositing(GetOrCreatePage(moreOptions, "Compositing"));

            var cloningPage = GetOrCreatePage(moreOptions, "Avatar Cloning");
            RenderAvatarCloning(cloningPage);
            WatchClones(() => RenderAvatarCloning(cloningPage));
    
            var personalisationPage = GetOrCreatePage(moreOptions, "Personalisation");
            RenderPersonalisation(personalisationPage);
            WatchPersonalisation(personalisationPage, () => RenderPersonalisation(personalisationPage));

            moreOptions.CreateBool("Capture Preview", Color.white, preferences.ShowPreview.Value, value =>
            {
                preferences.ShowPreview.Value = value;
                preferences.ManualSave();
            });

            RenderOutputPath(moreOptions);
        }

        void RenderPersonalisation(Page personalisationPage)
        {
            personalisationPage.RemoveAll();
            

            var accent = AccentColor.Current;
            var palette = preferences.AccentPalette.Value;
            
            personalisationPage.CreateHeader($"Colour", color: accent);

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

                // the label already says "Colour".
                personalisationPage.CreateEnum(" ", accent, swatch, value =>
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
            var hands = Player.HandsExist ? new[] { Player.LeftHand, Player.RightHand } : [];
            var spawnGunBarcodes = new List<string>();

            foreach (var hand in hands)
            {
                if (Trinkets.TryGetHeldCrate(hand, out var crate))
                    CreateChangeKeychain(crate.Barcode.ID);

                if (Trinkets.TryGetSpawnGunCrate(hand, out var selected) && !spawnGunBarcodes.Contains(selected.Barcode.ID))
                    spawnGunBarcodes.Add(selected.Barcode.ID);
            }

            CreateBarcodeSection("From your Spawn Gun...", spawnGunBarcodes);
            CreateBarcodeSection("From your clipboard...", Trinkets.GetClipboardBarcodes());

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

            return;

            void CreateBarcodeSection(string label, List<string> barcodes)
            {
                if (barcodes.Count == 0)
                    return;

                personalisationPage.CreateLabel(label);

                foreach (var barcode in barcodes)
                    CreateChangeKeychain(barcode);
            }

            void CreateChangeKeychain(string barcode)
            {
                canChange = true;

                personalisationPage.CreateFunction($"Change Keychain ({Trinkets.GetTitle(barcode)})", accent, () =>
                {
                    preferences.KeychainBarcode.Value = barcode;
                    preferences.ManualSave();
                });
            }
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

            rigPage.CreateHeader("Handheld Camera");
            
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

        void RenderCameraOverrides(Page overridesPage)
        {
            overridesPage.RemoveAll();

            var enabled = preferences.CameraOverrides.Value;

            overridesPage.CreateFunction(enabled ? "Disable Overrides" : "Enable Overrides", AccentColor.Current, () =>
            {
                preferences.CameraOverrides.Value = !enabled;
                preferences.ManualSave();
                RenderCameraOverrides(overridesPage);
            });

            if (!enabled)
                return;

            RenderFieldOfView(GetOrCreatePage(overridesPage, "Field of View"));
            RenderPostProcessingPrefs(GetOrCreatePage(overridesPage, "Post Processing"));
        }

        void RenderAvatarCloning(Page cloningPage)
        {
            cloningPage.RemoveAll();

            var delay = cloningPage.CreateFloat("Clone Delay", Color.white, preferences.CloneDelay.Value, .5f, 0f, 60f, value =>
            {
                preferences.CloneDelay.Value = value;
                preferences.ManualSave();
            });

            cloningPage.CreateBool("Include Held Items", Color.white, preferences.CloneIncludeHeldItems.Value, value =>
            {
                preferences.CloneIncludeHeldItems.Value = value;
                preferences.ManualSave();
            });

            if (FusionSync.IsAvailable)
            {
                var fusionSync = cloningPage.CreateBool("Fusion Sync", Color.white, AvatarClones.FusionSyncEnabled,
                    value => AvatarClones.FusionSyncEnabled = value);

                fusionSync.SetTooltip("Allows other players in the lobby to see your clones. " +
                                      "This needs to be re-enabled each time you launch the game.");
            }

            cloningPage.CreateFunction("Clone Avatar", Color.white, () =>
                AvatarClones.CloneLocalAfterDelay(preferences.CloneDelay.Value, preferences.CloneIncludeHeldItems.Value));

            var clones = AvatarClones.Local;

            if (clones.Count == 0)
            {
                return;
            }

            var managePage = GetOrCreatePage(cloningPage, "Manage Clones", $"Manage Clones ({clones.Count})");
            managePage.RemoveAll();

            for (var i = 0; i < clones.Count; i++)
            {
                var clone = clones[i];

                managePage.CreateConfirmFunction($"Clone {i + 1}", Color.white,
                    onConfirmed: () => AvatarClones.Delete(clone),
                    onArmed: () => CloneHighlighter.Show(clone),
                    onReset: () => CloneHighlighter.Hide(clone),
                    confirmText: "Delete?");
            }
        }

        void RenderFieldOfView(Page cameraPage)
        {
            cameraPage.RemoveAll();
            CreatePreviewButton(cameraPage, preferences, screenshotter);

            cameraPage.CreateFloat("Field of View", Color.white, preferences.FieldOfView.Value, 5f, 10f, 170f, value =>
            {
                preferences.FieldOfView.Value = value;
                preferences.ManualSave();
            });
            
            cameraPage.CreateFunction("Reset", Color.white, () =>
            {
                preferences.FieldOfView.Value = BoneSnapPreferences.DefaultFieldOfView;
                preferences.ManualSave();
                RenderFieldOfView(cameraPage);
            });
        }

        void RenderCompositing(Page compositingPage)
        {
            compositingPage.RemoveAll();

            var compositingMode = preferences.CompositingMode.Value;

            var compositingModeEntry = compositingPage.CreateDisplayEnum("Compositing Mode", Color.white, compositingMode,
                [
                    (CompositingMode.Combined, "Combined"),
                    (CompositingMode.Multiple, "Multiple"),
                    (CompositingMode.CookieCutout, "Cookie Cutout")
                ],
                value =>
                {
                    preferences.CompositingMode.Value = value;
                    RenderCompositing(compositingPage);
                });
            
            compositingModeEntry.SetTooltip("""
                                            <b>Changing the compositing mode allows you to output your Snap as multiple images.</b>
                                            Combined(1) - Regular BoneSnap output.
                                            Multiple(3) - Players, Background and Combined.
                                            Cookie Cutout(3) - Same as multiple - but this cuts the players out from the combined images. Worse transparency support, but retains scene lighting on characters.
                                            """);

            if (compositingMode == CompositingMode.Combined) 
                return;
            
            // BoneMenu tooltips truncate at 6 lines, so this can go here :P 
            var whichToUse = compositingPage.CreateLabel("Suggestion");
            whichToUse.SetTooltip("Don't need multiple layers? use <b>Combined</b>. Only use <b>Cookie Cutout</b> if you need shadows on your avatar cast from the scene, otherwise stick to <b>Multiple</b>.");
                
            compositingPage.CreateWarningLabel("Multi-layer captures are slow! Don't use it if you don't need it.");
        }

        void RenderPostProcessingPrefs(Page postProcessingPage)
        {
            postProcessingPage.RemoveAll();
            CreatePreviewButton(postProcessingPage, preferences, screenshotter);

            var preset = postProcessingPage.CreateEnum("Preset", Color.white, preferences.PostProcessingPreset.Value, value =>
            {
                preferences.PostProcessingPreset.Value = (PostProcessingPreset)value;
                preferences.ManualSave();
                RenderPostProcessingPrefs(postProcessingPage);
            });

            preset.SetTooltip(preferences.PostProcessingPreset.Value switch
            {
                PostProcessingPreset.Minimal => "The spectator camera's look, without Lens Distortion.",
                PostProcessingPreset.MatchHeadset => "Matches the player's view, without the spectator camera's own effects.",
                PostProcessingPreset.Custom => "Tweak individual settings, if you're a nerd :P",
                _ => "Default look of Bonelab's spectator camera."
            });

            if (preferences.PostProcessingPreset.Value != PostProcessingPreset.Custom)
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

    /// <summary>
    /// Creates a button that toggles the capture preview
    /// </summary>
    private static void CreatePreviewButton(Page page, BoneSnapPreferences preferences, Screenshotter screenshotter)
    {
        // Replaces the button from the page's last draw
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

        // Snapping takes over the preview, and the HUD is destroyed with the level
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

    /// <param name="title">Display name, for pages whose name changes. Defaults to <paramref name="name"/>.</param>
    private static Page GetOrCreatePage(Page parent, string name, string? title = null)
    {
        title ??= name;
        title = $"{title} <color=grey>›</color>";

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

    /// <summary>
    /// Keeps the Change Keychain buttons in sync with what's held/on the clipboard.
    /// </summary>
    private static void WatchPersonalisation(Page page, Action render)
    {
        _personalisationPage = page;
        _renderPersonalisation = render;

        if (_watching)
            return;

        _watching = true;

        Hooking.OnGrabObject += (_, _) => BeginRefresh();
        Hooking.OnReleaseObject += _ => BeginRefresh();
        Hooking.OnLevelLoaded += _ => BeginRefresh();

        BoneLib.BoneMenu.Menu.OnPageOpened += opened =>
        {
            if (opened == _personalisationPage)
                BeginRefresh();
        };

        return;
        
        void BeginRefresh() => CoroutineUtils.AfterFrame(RefreshPersonalisation);
    }

    public static void RefreshPersonalisation() => _renderPersonalisation?.Invoke();

    /// <summary>
    /// Keeps the clone count and Manage Clones page in sync as clones are created/deleted.
    /// </summary>
    private static void WatchClones(Action render)
    {
        var subscribe = _renderCloning == null;
        _renderCloning = render;

        if (subscribe)
            AvatarClones.Changed += () => _renderCloning?.Invoke();
    }
}
