using BoneLib;
using MelonLoader;
using BoneSnap.Assets;
using BoneSnap.Capture;
using BoneSnap.Personalisation;
using BoneSnap.SDK;
using BoneSnap.UI;

namespace BoneSnap;

internal partial class Main : MelonMod
{
    //private bool _isBindDown = false;
    private static BoneSnapPreferences _boneSnapPreferences;
    private static ReferenceCache _referenceCache = null!;
    private static Screenshotter _screenshotter = null!;
    public static Screenshotter Screenshotter => _screenshotter;
    public static BoneSnapPreferences Prefs => _boneSnapPreferences;

    public override void OnInitializeMelon()
    {
        var melonPreferencesCategory = MelonPreferences.CreateCategory("BoneSnap");
        _boneSnapPreferences = new BoneSnapPreferences(melonPreferencesCategory);
        _boneSnapPreferences.MainCapturePrefs.ResolutionScale.OnEntryValueChanged.Subscribe(
            (_, _) => SnapTaker.RefreshAllResolutionDisplays());
        SubscribePersonalisation(_boneSnapPreferences);

        _referenceCache = new ReferenceCache();
        _screenshotter = new Screenshotter(_referenceCache);

        BoneMenu.DrawBoneMenu(_boneSnapPreferences, _screenshotter);
        AssetManager.Initialize();
        base.OnInitializeMelon();
        Hooking.OnLevelUnloaded += OnLevelUnloaded;
    }

    private static void SubscribePersonalisation(BoneSnapPreferences prefs)
    {
        void OnAccentChanged()
        {
            AccentColor.Invalidate();
            SnapPersonalisation.RefreshLocalStyles();
            BoneMenu.RefreshPersonalisation();
        }

        void OnKeychainChanged()
        {
            Trinkets.Invalidate();
            SnapPersonalisation.RefreshLocalStyles();
            BoneMenu.RefreshPersonalisation();
        }

        prefs.AccentPalette.OnEntryValueChanged.Subscribe((_, _) => OnAccentChanged());
        prefs.AccentSwatch.OnEntryValueChanged.Subscribe((_, _) => OnAccentChanged());
        prefs.AccentHex.OnEntryValueChanged.Subscribe((_, _) => OnAccentChanged());
        prefs.KeychainBarcode.OnEntryValueChanged.Subscribe((_, _) => OnKeychainChanged());
        prefs.KeychainAdjustments.OnEntryValueChanged.Subscribe((_, _) => OnKeychainChanged());
    }

    private void OnLevelUnloaded()
    {
        _referenceCache.ClearReferences();
    }
        
    // public override void OnUpdate()
    // {
    //     if (Input.GetKey(KeyCode.JoystickButton8) && Input.GetKey(KeyCode.JoystickButton4))
    //     {
    //         if (_isBindDown) return;
    //         _isBindDown = true;
    //         _screenshotter.PrepareFisheyeCapture(0, _boneSnapPreferences.BindCapturePrefs.CaptureQuality.Value,
    //             _boneSnapPreferences.OutputPath.Value, _boneSnapPreferences.BindCapturePrefs.ResolutionScale.Value);
    //     }
    //     else
    //     {
    //         _isBindDown = false;
    //     }
    // }
}