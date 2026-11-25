using BoneLib.BoneMenu;
using BoneSnap.Capture;
using UnityEngine;

namespace BoneSnap.Menu.Pages;

internal static class PostProcessingPage
{
    public static void Render(Page postProcessingPage, BoneSnapPreferences preferences, Screenshotter screenshotter)
    {
        postProcessingPage.RemoveAll();
        BoneMenu.CreatePreviewButton(postProcessingPage, preferences, screenshotter);

        var preset = postProcessingPage.CreateEnum("Preset", Color.white, preferences.PostProcessingPreset.Value, value =>
        {
            preferences.PostProcessingPreset.Value = (PostProcessingPreset)value;
            preferences.ManualSave();
            Render(postProcessingPage, preferences, screenshotter);
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
}
