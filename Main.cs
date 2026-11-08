using BoneLib;
using MelonLoader;
using BoneLib.BoneMenu;
using UnityEngine;
using BoneSnap.Assets;
using BoneSnap.UI;

namespace BoneSnap;

internal partial class Main : MelonMod
{
    //private bool _isBindDown = false;
    private BoneSnapPreferences _boneSnapPreferences;

    public override void OnInitializeMelon()
    {
        var melonPreferencesCategory = MelonPreferences.CreateCategory("BoneSnap");
        _boneSnapPreferences = new BoneSnapPreferences(melonPreferencesCategory);
        BoneMenu.DrawBoneMenu(_boneSnapPreferences);
        AssetManager.Initialize();
        base.OnInitializeMelon();
        Hooking.OnLevelUnloaded += OnLevelUnloaded;
    }

    private static void OnLevelUnloaded()
    {
        ReferenceCache.ClearReferences();
    }

    // public override void OnUpdate()
    // {
    //     if (Input.GetKey(KeyCode.JoystickButton8) && Input.GetKey(KeyCode.JoystickButton4))
    //     {
    //         if (_isBindDown) return;
    //         _isBindDown = true;
    //         Screenshotter.PrepareFisheyeCapture(0, _boneSnapPreferences.BindCapturePrefs.CaptureQuality.Value,
    //             _boneSnapPreferences.OutputPath.Value, _boneSnapPreferences.BindCapturePrefs.ResolutionScale.Value);
    //     }
    //     else
    //     {
    //         _isBindDown = false;
    //     }
    // }
}