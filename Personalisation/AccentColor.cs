using System.Globalization;
using UnityEngine;

namespace BoneSnap.Personalisation;

public readonly record struct Swatch(string Name, Color Color);

/// <summary>
/// Catppuccin's accent names, shared by every flavour - for BoneMenu's swatch picker.
/// </summary>
public enum CatppuccinAccent
{
    Rosewater,
    Flamingo,
    Pink,
    Mauve,
    Red,
    Maroon,
    Peach,
    Yellow,
    Green,
    Teal,
    Sky,
    Sapphire,
    Blue,
    Lavender
}

/// <summary>
/// The player's accent colour - tinting their camera's body and UI, and BoneSnap's BoneMenu.
/// </summary>
internal static class AccentColor
{
    public const string DefaultSwatch = "Teal";

    // URP Lit's base colour
    public static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");

    // https://catppuccin.com/palette
    private static readonly Swatch[] Frappe =
    [
        new("Rosewater", Hex("F2D5CF")),
        new("Flamingo", Hex("EEBEBE")),
        new("Pink", Hex("F4B8E4")),
        new("Mauve", Hex("CA9EE6")),
        new("Red", Hex("E78284")),
        new("Maroon", Hex("EA999C")),
        new("Peach", Hex("EF9F76")),
        new("Yellow", Hex("E5C890")),
        new("Green", Hex("A6D189")),
        new("Teal", Hex("81C8BE")),
        new("Sky", Hex("99D1DB")),
        new("Sapphire", Hex("85C1DC")),
        new("Blue", Hex("8CAAEE")),
        new("Lavender", Hex("BABBF1"))
    ];

    private static readonly Swatch[] Latte =
    [
        new("Rosewater", Hex("DC8A78")),
        new("Flamingo", Hex("DD7878")),
        new("Pink", Hex("EA76CB")),
        new("Mauve", Hex("8839EF")),
        new("Red", Hex("D20F39")),
        new("Maroon", Hex("E64553")),
        new("Peach", Hex("FE640B")),
        new("Yellow", Hex("DF8E1D")),
        new("Green", Hex("40A02B")),
        new("Teal", Hex("179299")),
        new("Sky", Hex("04A5E5")),
        new("Sapphire", Hex("209FB5")),
        new("Blue", Hex("1E66F5")),
        new("Lavender", Hex("7287FD"))
    ];

    private static string? _currentHex;

    /// <summary>
    /// The accent colour from the player's preferences.
    /// </summary>
    public static Color Current => Resolve(Main.Prefs);

    public static string CurrentHex => _currentHex ??= ToHex(Current);

    public static string PaletteName(AccentPalette palette) => palette switch
    {
        AccentPalette.CatppuccinLatte => "Catppuccin Latte",
        AccentPalette.Hex => "Hex",
        _ => "Catppuccin Frappé"
    };

    public static Swatch[] GetSwatches(AccentPalette palette) => palette switch
    {
        AccentPalette.CatppuccinFrappe => Frappe,
        AccentPalette.CatppuccinLatte => Latte,
        _ => []
    };

    public static Swatch GetSwatch(AccentPalette palette, string name)
    {
        var swatches = GetSwatches(palette);
        return swatches.FirstOrDefault(s => s.Name == name) is { Name: not null } swatch
            ? swatch
            : swatches.First(s => s.Name == DefaultSwatch);
    }

    public static Color Resolve(BoneSnapPreferences prefs)
    {
        if (prefs.AccentPalette.Value != AccentPalette.Hex)
            return GetSwatch(prefs.AccentPalette.Value, prefs.AccentSwatch.Value).Color;

        return TryParseHex(prefs.AccentHex.Value, out var color)
            ? color
            : GetSwatch(AccentPalette.CatppuccinFrappe, DefaultSwatch).Color;
    }

    /// <summary>
    /// Wraps text in the accent colour, for BoneMenu's rich text.
    /// </summary>
    public static string Colorize(string text) => $"<color={CurrentHex}>{text}</color>";

    public static void Invalidate() => _currentHex = null;

    public static string ToHex(Color color)
    {
        Color32 c = color;
        return $"#{c.r:X2}{c.g:X2}{c.b:X2}";
    }

    /// <summary>
    /// Parses <c>#RGB</c> or <c>#RRGGBB</c>, with or without the <c>#</c>.
    /// </summary>
    public static bool TryParseHex(string? hex, out Color color)
    {
        color = default;

        if (string.IsNullOrWhiteSpace(hex))
            return false;

        hex = hex.Trim().TrimStart('#');

        if (hex.Length == 3)
            hex = string.Concat(hex.Select(c => $"{c}{c}"));

        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return false;

        color = new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
        return true;
    }

    private static Color Hex(string hex)
    {
        TryParseHex(hex, out var color);
        return color;
    }
}
