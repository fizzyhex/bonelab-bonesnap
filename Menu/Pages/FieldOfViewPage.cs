using BoneLib.BoneMenu;
using BoneSnap.Capture;
using UnityEngine;

namespace BoneSnap.Menu.Pages;

internal static class FieldOfViewPage
{
    public static void Render(Page cameraPage, BoneSnapPreferences preferences, Screenshotter screenshotter)
    {
        cameraPage.RemoveAll();
        BoneMenu.CreatePreviewButton(cameraPage, preferences, screenshotter);

        cameraPage.CreateFloat("Field of View", Color.white, preferences.FieldOfView.Value, 5f, 10f, 170f, value =>
        {
            preferences.FieldOfView.Value = value;
            preferences.ManualSave();
        });

        cameraPage.CreateFunction("Reset", Color.white, () =>
        {
            preferences.FieldOfView.Value = BoneSnapPreferences.DefaultFieldOfView;
            preferences.ManualSave();
            Render(cameraPage, preferences, screenshotter);
        });
    }
}
