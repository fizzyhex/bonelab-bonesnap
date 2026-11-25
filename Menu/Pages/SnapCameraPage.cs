using BoneLib;
using BoneLib.BoneMenu;
using BoneSnap.Downloading;
using BoneSnap.Personalisation;

namespace BoneSnap.Menu.Pages;

internal static class SnapCameraPage
{
    private static Action? _render;
    private static FunctionElement? _downloadButton;

    public static void Render(Page snapCameraPage)
    {
        string label;

        if (SnapCameraContent.IsDownloading)
            label = $"Downloading... ({SnapCameraContent.Progress * 100f:0}%)";
        else
            label = SnapCameraContent.IsInstalled ? "Redownload" : "Download";

        // Update the button in place to preserve the separately rendered
        // Personalisation link
        if (_downloadButton != null)
        {
            // Progress reports every frame
            if (_downloadButton.ElementName != label)
                _downloadButton.ElementName = label;

            return;
        }

        _downloadButton = snapCameraPage.CreateFunction(label, AccentColor.Current, SnapCameraContent.Download);
        _downloadButton.SetTooltip("Downloads the Snap Camera from mod.io. You'll need to be authenticated with mod.io through BONELAB for this to work.");
    }

    /// <summary>
    /// Refreshes the Snap Camera download button when download state changes and
    /// when a level loads.
    /// </summary>
    public static void Watch(Action render)
    {
        var subscribe = _render == null;
        _render = render;

        if (!subscribe)
            return;

        SnapCameraContent.Changed += () => _render?.Invoke();
        Hooking.OnLevelLoaded += _ => _render?.Invoke();
    }
}
