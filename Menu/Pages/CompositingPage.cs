using BoneLib.BoneMenu;
using UnityEngine;

namespace BoneSnap.Menu.Pages;

internal static class CompositingPage
{
    public static void Render(Page compositingPage, BoneSnapPreferences preferences)
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
                Render(compositingPage, preferences);
            });

        compositingModeEntry.SetTooltip("""
                                        <b>Changing the compositing mode allows you to output your Snap as multiple images.</b>
                                        Combined(1) - Regular BoneSnap output.
                                        Multiple(3) - Players, Background and Combined.
                                        Cookie Cutout(3) - Same as multiple - but this cuts the players out from the combined images. Worse transparency support, but retains scene lighting on characters.
                                        """);

        if (compositingMode == CompositingMode.Combined)
            return;

        // (BoneMenu truncates tooltips at 6 lines, so this is on a separate label)
        var whichToUse = compositingPage.CreateLabel("Suggestion");
        whichToUse.SetTooltip("Don't need multiple layers? use <b>Combined</b>. Only use <b>Cookie Cutout</b> if you need shadows on your avatar cast from the scene, otherwise stick to <b>Multiple</b>.");

        compositingPage.CreateWarningLabel("Multi-layer captures are slow! Don't use it if you don't need it.");
    }
}
