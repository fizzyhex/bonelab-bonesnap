using BoneLib.BoneMenu;
using BoneSnap.Capture;
using BoneSnap.Personalisation;

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

        FieldOfViewPage.Render(BoneMenu.GetOrCreatePage(overridesPage, "Field of View"), preferences, screenshotter);
        PostProcessingPage.Render(BoneMenu.GetOrCreatePage(overridesPage, "Post Processing"), preferences, screenshotter);
    }
}
