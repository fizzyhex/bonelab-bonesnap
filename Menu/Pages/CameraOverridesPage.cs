using BoneLib.BoneMenu;
using BoneSnap.Capture;
using BoneSnap.Personalisation;
using UnityEngine;

namespace BoneSnap.Menu.Pages;

internal static class CameraOverridesPage
{
    public static void Render(Page overridesPage, BoneSnapPreferences preferences, Screenshotter screenshotter)
    {
        overridesPage.RemoveAll();

        var enabled = preferences.CameraOverrides.Value;

        overridesPage.CreateFunction(enabled ? "Disable Overrides" : "Enable Overrides", AccentColor.Current, () =>
        {
            preferences.CameraOverrides.Value = !enabled;
            preferences.ManualSave();
            Render(overridesPage, preferences, screenshotter);
        });

        if (!enabled)
            return;

        var desktopView = overridesPage.CreateBool("Override Desktop View", Color.white, preferences.OverrideDesktopView.Value, value =>
        {
            preferences.OverrideDesktopView.Value = value;
            preferences.ManualSave();
        });

        desktopView.SetTooltip("BoneSnap's camera settings only affect your captures by default. " +
                               "Enable this if you want the camera settings to apply at all times!");

        FieldOfViewPage.Render(BoneMenu.GetOrCreatePage(overridesPage, "Field of View"), preferences, screenshotter);
        PostProcessingPage.Render(BoneMenu.GetOrCreatePage(overridesPage, "Post Processing"), preferences, screenshotter);
    }
}
