using BoneLib.BoneMenu;
using BoneSnap.Personalisation;
using UnityEngine;

namespace BoneSnap.Menu.Pages;

internal static class PersonalisationPage
{
    public static void Render(Page personalisationPage, BoneSnapPreferences preferences, Action onAccentChanged)
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
                    Render(personalisationPage, preferences, onAccentChanged);
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

            // Leave the label blank because the header already says
            // "Colour".
            personalisationPage.CreateEnum(" ", accent, swatch, value =>
            {
                preferences.AccentSwatch.Value = value.ToString();
                OnAccentChanged();
            });
        }

        return;

        void OnAccentChanged()
        {
            preferences.ManualSave();
            onAccentChanged();
        }
    }
}
