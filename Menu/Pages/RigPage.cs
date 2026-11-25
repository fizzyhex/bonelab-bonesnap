using BoneLib.BoneMenu;
using UnityEngine;

namespace BoneSnap.Menu.Pages;

internal static class RigPage
{
    public static void Render(Page rigPage, BoneSnapPreferences preferences)
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
}
