using BoneLib.BoneMenu;
using UnityEngine;

namespace BoneSnap.Menu.Pages;

internal static class SteamPage
{
    public static void Render(Page steamPage, BoneSnapPreferences preferences)
    {
        steamPage.RemoveAll();

        var saveToSteam = steamPage.CreateBool("Save to Steam", Color.white, preferences.SaveToSteam.Value, value =>
        {
            preferences.SaveToSteam.Value = value;
            preferences.ManualSave();
        });

        var steamLayer = steamPage.CreateDisplayEnum("Layer", Color.white, preferences.SteamLayer.Value,
            [
                (SteamLayer.Players, "Players"),
                (SteamLayer.Combined, "Combined"),
                (SteamLayer.Background, "Background")
            ],
            value =>
            {
                preferences.SteamLayer.Value = value;
                preferences.ManualSave();
            });

        steamLayer.SetTooltip("Changes which layer is saved to Steam if you have compositing enabled.");
    }
}
