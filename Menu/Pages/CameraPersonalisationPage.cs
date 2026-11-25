using BoneLib;
using BoneLib.BoneMenu;
using BoneSnap.Personalisation;
using UnityEngine;

namespace BoneSnap.Menu.Pages;

/// <summary>
/// Keychain trinket selection for the Snap Camera.
/// </summary>
internal static class CameraPersonalisationPage
{
    private static bool _watching;
    private static Action? _render;
    private static Page? _page;
    private static string? _adjustingBarcode;

    public static void Render(Page personalisationPage, BoneSnapPreferences preferences)
    {
        personalisationPage.RemoveAll();

        var accent = AccentColor.Current;
        var canChange = false;
        var hands = Player.HandsExist ? new[] { Player.LeftHand, Player.RightHand } : [];
        var spawnGunBarcodes = new List<string>();

        foreach (var hand in hands)
        {
            if (Trinkets.TryGetHeldCrate(hand, out var crate))
                CreateChangeKeychain(crate.Barcode.ID);

            if (Trinkets.TryGetSpawnGunCrate(hand, out var selected) && !spawnGunBarcodes.Contains(selected.Barcode.ID))
                spawnGunBarcodes.Add(selected.Barcode.ID);
        }

        CreateBarcodeSection("From your Spawn Gun...", spawnGunBarcodes);
        CreateBarcodeSection("From your clipboard...", Trinkets.GetClipboardBarcodes());

        var current = preferences.KeychainBarcode.Value;
        var nothingSelected = string.IsNullOrEmpty(current);

        if (nothingSelected && !canChange)
            personalisationPage.CreateLabel("Grab an item to select a custom keychain trinket!");

        if (nothingSelected)
            return;

        personalisationPage.CreateHeader($"Trinket - {Trinkets.GetTitle(current)}");

        var adjustPage = BoneMenu.GetOrCreatePage(personalisationPage, "Adjust");

        if (current != _adjustingBarcode || adjustPage.Elements.Count == 0)
            RenderTrinketAdjust(adjustPage, preferences, current);

        personalisationPage.CreateFunction($"Remove", Color.white, () =>
        {
            preferences.KeychainBarcode.Value = string.Empty;
            preferences.ManualSave();
        });

        return;

        void CreateBarcodeSection(string label, List<string> barcodes)
        {
            if (barcodes.Count == 0)
                return;

            personalisationPage.CreateLabel(label);

            foreach (var barcode in barcodes)
                CreateChangeKeychain(barcode);
        }

        void CreateChangeKeychain(string barcode)
        {
            canChange = true;

            personalisationPage.CreateFunction($"Change Keychain ({Trinkets.GetTitle(barcode)})", accent, () =>
            {
                preferences.KeychainBarcode.Value = barcode;
                preferences.ManualSave();
            });
        }
    }

    private static void RenderTrinketAdjust(Page adjustPage, BoneSnapPreferences preferences, string barcode)
    {
        adjustPage.RemoveAll();
        _adjustingBarcode = barcode;

        var adjustment = Trinkets.GetAdjustment(preferences, barcode);

        void Save()
        {
            Trinkets.SetAdjustment(preferences, barcode, adjustment);
            preferences.ManualSave();
        }

        adjustPage.CreateLabel($"Adjusting {Trinkets.GetTitle(barcode)}");

        adjustPage.CreateFloat("Scale", Color.white, adjustment.Scale, .1f, .1f, 5f, value =>
        {
            adjustment.Scale = value;
            Save();
        });

        const float cm = 100f;

        adjustPage.CreateFloat("Offset X (cm)", Color.white, adjustment.OffsetX * cm, .5f, -10f, 10f, value =>
        {
            adjustment.OffsetX = value / cm;
            Save();
        });

        adjustPage.CreateFloat("Offset Y (cm)", Color.white, adjustment.OffsetY * cm, .5f, -10f, 10f, value =>
        {
            adjustment.OffsetY = value / cm;
            Save();
        });

        adjustPage.CreateFloat("Offset Z (cm)", Color.white, adjustment.OffsetZ * cm, .5f, -10f, 10f, value =>
        {
            adjustment.OffsetZ = value / cm;
            Save();
        });

        adjustPage.CreateInt("Rotation X", Color.white, (int)adjustment.RotationX, 15, -180, 180, value =>
        {
            adjustment.RotationX = value;
            Save();
        });

        adjustPage.CreateInt("Rotation Y", Color.white, (int)adjustment.RotationY, 15, -180, 180, value =>
        {
            adjustment.RotationY = value;
            Save();
        });

        adjustPage.CreateInt("Rotation Z", Color.white, (int)adjustment.RotationZ, 15, -180, 180, value =>
        {
            adjustment.RotationZ = value;
            Save();
        });

        adjustPage.CreateFunction("Reset", Color.white, () =>
        {
            Trinkets.ResetAdjustment(preferences, barcode);
            preferences.ManualSave();
            RenderTrinketAdjust(adjustPage, preferences, barcode);
        });
    }

    /// <summary>
    /// Keeps the Change Keychain buttons in sync with what's held/on the clipboard.
    /// </summary>
    public static void Watch(Page page, Action render)
    {
        _page = page;
        _render = render;

        if (_watching)
            return;

        _watching = true;

        Hooking.OnGrabObject += (_, _) => BeginRefresh();
        Hooking.OnReleaseObject += _ => BeginRefresh();
        Hooking.OnLevelLoaded += _ => BeginRefresh();

        BoneLib.BoneMenu.Menu.OnPageOpened += opened =>
        {
            if (opened == _page)
                BeginRefresh();
        };

        return;

        void BeginRefresh() => CoroutineUtils.AfterFrame(Refresh);
    }

    public static void Refresh() => _render?.Invoke();
}
